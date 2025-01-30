import datetime
import threading
import time
import requests
import schedule
from prometheus_client import start_http_server
from prometheus_client import Gauge
from flask import *
import click
import json
import pymssql

#This script was developed to enable "push" monitoring of Crestron Fusion, Dante Domain Manager, and Biamp Sagevue.
#The script enables alerts to be sent to REST endpoints; specifically, zoom webhooks in the current iteration.
#The script also enables the visualization of metrics through the use of Prometheus/Grafana.

#region Script Data

# Flask server for adjusting script behavior.
AlertREST = Flask(__name__)

# Script startup alert behavior (should alerts be automatically enabled upon script launch)?
SEND_ALERTS = False

# Polling frequency (in seconds).
PROG_REFRESH_FREQUENCY = 15

# Keywords to ignore when sending out alerts.
BLACKLISTED_KEYWORDS = ["ExampleKeyword1", "Test Devices"]

# Enable/Disable messages
ALERT_NOTIF_DISABLE = "Alerting notifications are now disabled."
ALERT_NOTIF_ENABLE = "Alerting notifications are now enabled."

# Open the json file containing connection information/tokens/passwords.
with open('MonitoringData.json', 'r') as file:
    
    # Load the json data to be used in the script.
    script_variables = json.load(file)


#endregion

#region DDM Info

# Full URL of the DDM server API.
DDM_URL = script_variables['DDM_URL']

# DDM API key, generated from the web interface.
DDM_API_KEY = script_variables['DDM_API_Key']

# Lists for storing DDM related data.
DDM_DOMAINS = []
DDM_DEVICES = []
DDM_ERROR_LIST = []
DDM_OFFLINE_LIST = []
DDM_TEMP_LIST = []
DDM_TEMP_OFFLINE_LIST = []

# List for storing temporarily muted keywords.
ALERTS_TEMP_IGNORE = []

# Used for batching alerts, to not spam the webhook whenever a large amount of alerts are sent out at once.
ALERTS_NOTIF_LIST = []

# Prometheus DDM gauges, used for the graphical display of active issues.
DDM_Total_Errors = Gauge('ddm_total_errors', 'Total errors across DDM.')
DDM_Missing_Devices = Gauge('ddm_missing_devices', 'Number of devices not currently connected to the DDM server.')

# DDM API query for requesting all active domains.
getDomains = """query Domains{
    domains {
        name
    }
}"""

# DDM API query for requesting all devices in a specific domain.
getDevices = """query Devices($name: String) {
    domain(name: $name) {
        devices {
            name
            status {
                connectivity
                subscriptions
                latency
                clocking
            }
        }
    }
}"""

#endregion

#region SageVue Info

SageVue_SessionID = 'Placeholder'
SageVue_Error_List = []
SageVue_Offline_List = []

SageVue_Auth_Query_Endpoint = script_variables['SageVue_Auth_Query_Endpoint']
SageVue_Login_Endpoint = script_variables['SageVue_Login_Endpoint']
SageVue_Systems_Endpoint = script_variables['SageVue_Systems_Endpoint']
SageVue_Devices_Endpoint = script_variables['SageVue_Devices_Endpoint']

SageVue_Username = script_variables['SageVue_Username']
SageVue_Password = script_variables['SageVue_Password']

Sagevue_Total_Errors = Gauge('sagevue_total_errors', 'Total errors across all Tesira systems.')
Sagevue_Missing_Devices = Gauge('sagevue_missing_devices', 'Number of devices not currently connected to the server.')

#endregion

#region Fusion info

FusionSQLServer = script_variables['Fusion_SQL_Server']
FusionSQLUsername = script_variables['Fusion_SQL_Username']
FusionSQLPassword = script_variables['Fusion_SQL_Password']

Fusion_Error_List = []
Fusion_Offline_List = []
Fusion_Help_Request_List = []

#endregion

#region Channels

#Zoom webhook endpoint for sending various types of device alerts.
alertChannel = script_variables['Alert_Channel']
alertChannelToken = script_variables['Alert_Channel_Token']

#Zoom webhook endpoint for sending script specific notifications (such as REST responses and errors).
notifyChannel = script_variables['Notify_Channel']
notifyToken = script_variables['Notify_Channel_Token']

#Zoom webhook endpoint for sending daily reports (both SOD and EOD).
dailyReportChannel = script_variables['Daily_Report_Channel']
dailyReportToken = script_variables['Daily_Report_Channel_Token']

#Zoom webhook endpoint for sending fusion help requests.
helpRequestChannel = script_variables['Help_Request_Channel']
helpRequestToken = script_variables['Help_Request_Channel_Token']

#Allows for the swapping of channels when necessary. These are the variables used in the script.
usingAlertChannel = alertChannel
usingAlertChannelToken = alertChannelToken

usingSODChannel = dailyReportChannel
usingSODToken = dailyReportToken

usingNotifChannel = notifyChannel
usingNotifToken = notifyToken

usingHelpRequestChannel = helpRequestChannel
usingHelpRequestToken = helpRequestToken

#endregion

#region Script Methods
def prog_thread():
    #Fetch the domains at script start.
    get_dante_domains()

    #Infinite loop, iterate over each step after sleeping for the specified amount of time.
    while True:
        try:
            fusion_thread()
            sage_vue_thread()
            ddm_thread()
        except Exception as e:
            click.echo(e)
        
        #If any new alerts were generated, send them out.
        send_alert_notif()
        
        #Run any pending scheduled events.
        schedule.run_pending()

        #Pause the script execution.
        time.sleep(PROG_REFRESH_FREQUENCY)

#Starts the main script thread.
def start_services():
    threading.Thread(target=prog_thread).start()

#Enables alerts to be sent to the proper endpoints.
def enable_alerts(send_notif):
    global SEND_ALERTS
    SEND_ALERTS = True
    if send_notif:
        send_notification(ALERT_NOTIF_ENABLE)


#Disables alerts from being sent to any endpoints, except the script information channel.
def disable_alerts(send_notif):
    global SEND_ALERTS
    SEND_ALERTS = False
    if send_notif:
        send_notification(ALERT_NOTIF_DISABLE)

#Prevents alerts from being sent based on the content of the alert message.
def mute_thread(args):
    #args[0] -> mute keyword
    #args[1] -> mute length (in minutes)

    #Only numeric values are permitted for time parameter.
    if not str.isnumeric(args[1]):
        send_notification("Cannot mute alerts containing " + args[0] + " - time parameter is invalid.")
    else:
        try:
            send_notification("Muting alerts containing " + args[0] + " for " + args[1] + " minutes.")
            ALERTS_TEMP_IGNORE.append(args[0])
            time.sleep(int(args[1])*60)
            ALERTS_TEMP_IGNORE.remove(args[0])
            send_notification("Alerts containing " + args[0] + " are no longer muted!")
        except:
            send_notification("Unknown error muting alerts containing " + args[0] + ".")
            return "Error"
    return "Success"

#Adds an alert to the list of upcoming alerts to send out.
def add_to_alert_notif_push(category, device, message):
    if not any(ext in device for ext in ALERTS_TEMP_IGNORE) and not any(ext in message for ext in ALERTS_TEMP_IGNORE):
        ALERTS_NOTIF_LIST.append(category + " - " + device + " - " + message)
#endregion


#region REST Endpoints

#REST endpoint for running a report.
@AlertREST.route("/RunReport", methods=['GET'])
def http_report():
    run_report()
    return "Success"

#REST endpoint for enabling alerts.
@AlertREST.route("/EnableAlerts", methods=['GET'])
def http_enable():
    enable_alerts(True)
    return "Success"

#REST endpoint for disabling alerts.
@AlertREST.route("/DisableAlerts", methods=['GET'])
def http_disable():
    disable_alerts(True)
    return "Success"

#REST endpoint for manually muting alerts for a specific amount of time that contain a given keyword.
@AlertREST.route("/ManualMute", methods=['GET'])
def manual_mute():
    try:
        keyword = request.args.get('keyword')
        length = request.args.get('length')
        data = [keyword, length]
        threading.Thread(target=mute_thread, args=(data,)).start()
        return "Success"
    except:
        return "An unknown error occurred while processing mute request. Please check parameters and try again."

#endregion

#region DDM Functions

#
def ddm_thread():
    
    #Clear the temporary lists holding previous alerts.
    DDM_TEMP_LIST.clear()
    DDM_TEMP_OFFLINE_LIST.clear()
    try:
        #Iterate through each domain and fetch all the devices in each domain.
        for domain in DDM_DOMAINS:
            variables = {'name': domain}
            result = run_ddm_query(getDevices, 200, variables)
            
            #Iterate through each device and check each for specific types of errors.
            for device in result["data"]["domain"]["devices"]:
                
                #Connectivity error (Device offline).
               if device["status"]["connectivity"] == "ERROR":
                connectivity_string = domain + " - " + device["name"] + " is offline."
                DDM_TEMP_OFFLINE_LIST.append(connectivity_string)
                if connectivity_string not in DDM_OFFLINE_LIST:
                    DDM_OFFLINE_LIST.append(connectivity_string)
                    add_to_alert_notif_push("DDM", domain, device["name"] + " is offline.")

                #Subsciption error (flow is not connected)
                if device["status"]["subscriptions"] == "ERROR":
                    sub_string = domain + " - " + device["name"] + " has a subscription error."
                    DDM_TEMP_LIST.append(sub_string)
                    if sub_string not in DDM_ERROR_LIST:
                        DDM_ERROR_LIST.append(sub_string)
                        add_to_alert_notif_push("DDM", domain, device["name"] + " has a subscription error.")
                
                #Latency error.
                if device["status"]["latency"] == "ERROR":
                    latency_string = domain + " - " + device["name"] + " has a latency error."
                    DDM_TEMP_LIST.append(latency_string)
                    if latency_string not in DDM_ERROR_LIST:
                        DDM_ERROR_LIST.append(latency_string)
                        add_to_alert_notif_push("DDM", domain, device["name"] + " has a latency error.")

                #Clocking error.
                if device["status"]["clocking"] == "ERROR":
                    clock_string = domain + " - " + device["name"] + " has a clocking error."
                    DDM_TEMP_LIST.append(clock_string)
                    if clock_string not in DDM_ERROR_LIST:
                        DDM_ERROR_LIST.append(clock_string)
                        add_to_alert_notif_push("DDM", domain, device["name"] + " has a clocking error.")
                            
    except Exception as e:
        click.echo(repr(e))
        
    #Iterate through the list of errors, and remove any that are no longer active.
    for error in DDM_ERROR_LIST:
        if error not in DDM_TEMP_LIST:
            DDM_ERROR_LIST.remove(error)

    #Iterate through the list of offline devices, and remove any that are back online.
    for device in DDM_OFFLINE_LIST:
        if device not in DDM_TEMP_OFFLINE_LIST:
            DDM_OFFLINE_LIST.remove(device)

    #Update the prometheus metrics 
    DDM_Missing_Devices.set(len(DDM_OFFLINE_LIST))
    DDM_Total_Errors.set(len(DDM_ERROR_LIST))

#Function to format the DDM error and offline lists into a single string return value.
def get_ddm_status_message():
    temp_string = ""
    for x in range(len(DDM_ERROR_LIST)):
        temp_string = temp_string + DDM_ERROR_LIST[x]
        if x < len(DDM_ERROR_LIST) - 1:
            temp_string = temp_string + "\r"
    for x in range(len(DDM_OFFLINE_LIST)):
        temp_string = temp_string + DDM_OFFLINE_LIST[x]
        if x < len(DDM_OFFLINE_LIST) - 1:
            temp_string = temp_string + "\r"
    if len(DDM_ERROR_LIST) == 0 and len(DDM_OFFLINE_LIST) == 0:
        return "No errors to report."
    else:
        return temp_string

#Function to get the current DDM domains.
def get_dante_domains():
    DDM_DOMAINS.clear()
    temp_domains = run_ddm_query(getDomains, 200, {})
    for key in temp_domains["data"]["domains"]:
        DDM_DOMAINS.append(key["name"])

#Function to run a query on the DDM API.
def run_ddm_query(query, status_code, variables):
    new_request = requests.post(DDM_URL, json={'query': query, 'variables': variables}, headers={"Authorization": DDM_API_KEY})
    if new_request.status_code == status_code:
        return new_request.json()
    else:
        raise Exception(f"Unexpected status code returned: {new_request.status_code}")
# endregion

#region SageVue Functions
def sage_vue_thread():
    global SageVue_SessionID
    try:
        # Check API key. If invalid, generate new key.
        api_check = requests.get(SageVue_Auth_Query_Endpoint, headers={'SessionID': SageVue_SessionID}, timeout=5)

        if api_check.status_code == 400 or api_check.status_code == 401:
            #Invalid status return. Use the login endpoint to generate a new session ID.
            session_id = requests.post(SageVue_Login_Endpoint, json={"credentials": {'username': SageVue_Username, 'password': SageVue_Password}})
            SageVue_SessionID = session_id.json()['LoginId']

        # Request the systems and devices
        api_systems = requests.get(SageVue_Systems_Endpoint, headers={'SessionID': SageVue_SessionID}, timeout=5)
        api_devices = requests.get(SageVue_Devices_Endpoint, headers={'SessionID': SageVue_SessionID}, timeout=5)

        # Temporary variable for storing systems
        systems = api_systems.json()['Systems']

        temp_error_list = []
        
        # Iterate through the systems.
        # For each system status that is not "Green", list out each fault the system currently has, and append to error list.
        for system in systems:
            if system['Status'] != "Green":
                for fault in system["Faults"]:
                    error_id = system["Description"] + " - " + fault["Message"]
                    temp_error_list.append(error_id)

                    # Send alerts for new errors.
                    # Block out Dante Mute and NTP errors. 
                    # In a sufficiently large network, these types of alerts are guaranteed and not a priority.
                    if error_id not in SageVue_Error_List and "DAN1" not in fault["Message"] and "NTP" not in fault["Message"]:
                        SageVue_Error_List.append(error_id)
                        add_to_alert_notif_push("SageVue", system["Description"], fault["Message"])

        # If the error no longer exists, remove from the list. This prevents duplicate errors from sending out alerts.
        for error in SageVue_Error_List:
            if error not in temp_error_list:
                SageVue_Error_List.remove(error)

        # Set the prometheus metrics with the new information.
        Sagevue_Total_Errors.set(len(SageVue_Error_List))
        Sagevue_Missing_Devices.set(len(api_devices.json()['Errors']))

    except Exception as e:
        click.echo(repr(e))


def get_sage_vue_status_message():
    #Build out the error string with all currently active alerts.
    if len(SageVue_Error_List) == 0:
        return "No errors to report."
    
    temp_string = ""
    temp_index = len(SageVue_Error_List)
    for x in range(temp_index):
        #Append each error to the new string.
        temp_string = temp_string + SageVue_Error_List[x]
        if x != temp_index - 1:
            #If more errors exist, add a carriage return.
            temp_string = temp_string + "\r"
            
    return temp_string

# endregion

#region Fusion Functions
def fusion_thread():
    
    #Set up the Fusion database connection using the fusion credentials.
    conn = pymssql.connect(FusionSQLServer, FusionSQLUsername, FusionSQLPassword, "CrestronFusion")
    cursor = conn.cursor(as_dict=True)

    #Get all Fusion Rooms that are offline.
    cursor.execute('SELECT RoomName FROM CRV_Rooms '
                   'WHERE RoomID IN (SELECT RoomID FROM CRV_RoomAttributeValues '
                   'WHERE AttributeID = %s AND RawAnalogValue != 2)', 'ONLINE_STATUS')
     
    #Temporary list to hold offline rooms.
    temp_offline = []
    
    for room in cursor:
        #For each offline room:
            #Check if this room is not in the global offline list.
            #If not, check if the room name is blacklisted.
            #If not blacklisted, add this room to the offline list.
            #Also add to the list of alerts to send out.
        temp_offline.append(room['RoomName'])
        if room['RoomName'] not in Fusion_Offline_List:
            if not any(ext in room['RoomName'] for ext in BLACKLISTED_KEYWORDS):
                Fusion_Offline_List.append(room['RoomName'])
                add_to_alert_notif_push("Fusion", room['RoomName'], "Room has gone offline")
                
    #If room has not been previously detected as offline, and was not in the query response, remove from the room offline list.
    for room in Fusion_Offline_List:
        if room not in temp_offline:
            Fusion_Offline_List.remove(room)

    #Get all Fusion Rooms that currently have error alerts.
    cursor.execute('SELECT CRV_Rooms.RoomName, CRV_Rooms.RoomID FROM CRV_RoomAttributeValues '
                   'FULL OUTER JOIN CRV_Rooms ON CRV_RoomAttributeValues.RoomID = CRV_Rooms.RoomID '
                   'WHERE CRV_RoomAttributeValues.AttributeID = %s AND CRV_RoomAttributeValues.RawAnalogValue != 0','ERROR_ALERT')

    #Reset the temporary list.
    temp_error = []
    
    #For each room, gather the room name and room ID and place them in the list.
    for room in cursor:
        temp_error.append([room['RoomName'], room['RoomID']])
    
    #Using the ID from the query response, execute a new query to get the specific error message from the room.
    for room in temp_error:
        cursor.execute('SELECT RawSerialValue FROM CRV_RoomAttributeValues '
                       'WHERE AttributeID = %s AND RoomID = %s',
                       ('ERROR_MESSAGE', room[1]))
        
        #Append the error message to the room data list.
        for x in cursor:
            room.append(x['RawSerialValue'])

    #For each room with errors:
        #Check if this room is not in the global fusion error list.
        #If not, check if the error message contains the indicator of an "ok" or a "notice".
        #If not, add this room to the room error list.
        #Also add to the list of alerts to send out.       
    for room in temp_error:
        if room not in Fusion_Error_List:
            if "1:notice" not in room[2] and "0:ok" not in room[2]:
                Fusion_Error_List.append(room)
                add_to_alert_notif_push("Fusion", room[0], room[2])

    #If room has not been previously detected as having errors, and was not in the query response, remove from the room error list.
    for room in Fusion_Error_List:
        if room not in temp_error:
            Fusion_Error_List.remove(room)

    #Get all Fusion Rooms that currently have help requests.
    cursor.execute(
        'SELECT CRV_Rooms.RoomName, CRV_Rooms.RoomID FROM CRV_RoomAttributeValues '
        'FULL OUTER JOIN CRV_Rooms ON CRV_RoomAttributeValues.RoomID = CRV_Rooms.RoomID '
        'WHERE CRV_RoomAttributeValues.AttributeID = %s AND CRV_RoomAttributeValues.RawDigitalValue != 0', 'HELP_ALERT')

    #Reset the temporary list.
    temp_error = []

    #For each room, gather the room name and room ID and place them in the list.
    for room in cursor:
        temp_error.append([room['RoomName'], room['RoomID']])

    #Using the ID from the query response, execute a new query to get the specific help request message from the room.
    for room in temp_error:
        cursor.execute('SELECT RawSerialValue FROM CRV_RoomAttributeValues '
                       'WHERE AttributeID = %s AND RoomID = %s',
                       ('HELP_MESSAGE', room[1]))

        #Append the error message to the room data list.
        for x in cursor:
            room.append(x['RawSerialValue'])

    #For each room with errors:
        #Check if this room is not in the global fusion help request list.
        #If not, add this room to the room error list.
        #Also add to the list of help request alerts to send out.  
    for room in temp_error:
        if room not in Fusion_Help_Request_List:
            Fusion_Help_Request_List.append(room)
            send_help_request(room[0], room[2])
    for room in Fusion_Help_Request_List:
        if room not in temp_error:
            Fusion_Help_Request_List.remove(room)


def get_fusion_status_message():
    
    #Nothing to report from fusion.
    if len(Fusion_Error_List) == 0 and len(Fusion_Offline_List) == 0:
        return "No errors to report."
    
    #Gather all offline rooms and rooms with errors, and place in a list.
    temp_error = []
    for room in Fusion_Error_List:
        temp_error.append(room[0] + " - " + room[2])
    for room in Fusion_Offline_List:
        temp_error.append(room + " - " "Room is currently offline")
    temp_index = len(temp_error)
    temp_string = ""
    
    #Append each item in the list to a string, then return the string.
    for x in range(temp_index):
        temp_string = temp_string + temp_error[x]
        if x != temp_index - 1:
            #If more errors exist, add a carriage return.
            temp_string = temp_string + "\r"
    return temp_string

#endregion

#region Zoom POST Functions
def send_notification(message):
    requests.post(usingNotifChannel,
                  headers={'Authorization': usingNotifToken, "Content-Type": "application/json"},
                  json={
                      "head": {
                          "text": "Program Notification",
                          "style": {
                              "color": "#9d0bf4"
                          }
                      },
                      "body": [
                          {
                              "type": "message",
                              "text": message,
                              "style": {
                                  "color": "#305acf"
                              }
                          }
                      ]
                  })
    
def send_help_request(room, message):
    if SEND_ALERTS:
        requests.post(usingHelpRequestChannel,
                      headers={'Authorization': usingHelpRequestToken, "Content-Type": "application/json"},
                      json={
                          "head": {
                              "text": "Help Request Alert",
                              "style": {
                                  "color": "#00FF00"
                              }
                          },
                          "body": [
                              {
                                  "type": "message",
                                  "text": "Room - " + room
                              },
                              {
                                  "type": "message",
                                  "text": "Customer says: " + message,
                                  "style": {
                                      "color": "#449FD4",
                                      "sidebar_color": "#449FD4"
                                  }
                              }
                          ]
                      }
        )
        
def send_alert_notif():
    if SEND_ALERTS and len(ALERTS_NOTIF_LIST) > 0:
        temp_string = ""
        temp_index = len(ALERTS_NOTIF_LIST)
        for x in range(temp_index):
            temp_string = temp_string + ALERTS_NOTIF_LIST[x]
            if x != temp_index - 1:
                temp_string = temp_string + "\r"
        requests.post(usingAlertChannel,
                      headers={'Authorization': usingAlertChannelToken, "Content-Type": "application/json"},
                      json={
                          "head": {
                              "text": "Device Alert",
                              "style": {
                                  "color": "#C107EB"
                              }
                          },
                          "body": [
                              {
                                  "type": "message",
                                  "text": (str(len(ALERTS_NOTIF_LIST)) + " new device alert") + ("s." if len(ALERTS_NOTIF_LIST) > 1 else ".")
                              },
                              {
                                  "type": "message",
                                  "text": temp_string,
                                  "style": {
                                      "color": "#C107EB",
                                      "sidebar_color": "#C107EB"
                                  }
                              }
                          ]
                      })
    ALERTS_NOTIF_LIST.clear()

def craft_report_section(service, content):
    return {
        "type": "section",
        "sidebar_color": "#00FF00" if content == "No errors to report." or content == "No mics with low batteries." else "#C107EB",
        "sections": [
            {
                "type": "message",
                "text": "--- " + service + " --- ",
                "style": {
                    "color": "#0000FF"
                }
            },
            {
                "type": "message",
                "text": content,
                "style": {
                    "color": "#00FF00" if content == "No errors to report." or content == "No mics with low batteries." else "#C107EB"
                }
            }

        ]
    }

def run_report():
    sagevue = get_sage_vue_status_message()
    ddm = get_ddm_status_message()
    fusion = get_fusion_status_message()
    requests.post(usingSODChannel,
                  headers={'Authorization': usingSODToken, "Content-Type": "application/json"},
                  json={
                      "head": {
                          "text": "SoD Report - " + str(datetime.date.today()),
                          "style": {
                              "color": "#00DD00"
                          }
                      },
                      "body": [
                          craft_report_section("SageVue", sagevue),
                          craft_report_section("Dante Domain Manager", ddm),
                          craft_report_section("Fusion", fusion)
                      ]
                  })

#endregion

if __name__ == "__main__":
    #Fetches the updated list of DDM domains.
    schedule.every().day.at(script_variables['DDM_Domain_Fetch_Time']).do(get_dante_domains)
    
    #Enables the alerts.
    schedule.every().day.at(script_variables['Enable_Alerts_Time']).do(lambda: enable_alerts(False))
    
    #Runs a report, detailing all the currently active alerts from each platform.
    schedule.every().day.at(script_variables['Run_Report_Time']).do(run_report)

    #Disable the alerts.
    schedule.every().day.at(script_variables['Disable_Alerts_Time']).do(lambda: disable_alerts(False))
    
    #Start the Prometheus server to enable metrics to be pulled.
    start_http_server(8000)
    
    #Start the script loop, which executes on a polling interval.
    start_services()
    
    #Start the FLASK server, to allow for external control.
    AlertREST.run(host="0.0.0.0", port=2030)
