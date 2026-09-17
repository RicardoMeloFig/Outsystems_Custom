#if canImport(Cordova)
import Cordova
#endif

import Foundation
import OSFirebaseMessagingLib

@objc(OSFirebaseCloudMessaging)
class OSFirebaseCloudMessaging: CDVPlugin {
    private var plugin: FirebaseMessagingController?
    private var firebaseAppDelegate: FirebaseMessagingApplicationDelegate = .shared
    
    private var deviceReady: Bool = false
    private var eventQueue: [String]?
    
    override func pluginInitialize() {
        #if SWIFT_PACKAGE
        // In SPM builds the .momd resource lives in this module's bundle, not in Bundle.main.
        // Set resourceBundle before FirebaseMessagingController is created so CoreData picks
        // it up on first access (static let model is lazily initialised).
        CoreDataManager.resourceBundle = Bundle.module
        #endif
        self.plugin = FirebaseMessagingController()
        self.firebaseAppDelegate.eventDelegate = self
    }
    
    @objc(ready:)
    func ready(command: CDVInvokedUrlCommand) {
        self.deviceReady = true
        
        if let eventQueue = self.eventQueue {
            self.commandDelegate.run { [weak self] in
                guard let self else { return }
                
                for js in eventQueue {
                    self.commandDelegate.evalJs(js)
                }
                self.eventQueue = nil
            }
        }
    }
    
    @objc(registerDevice:)
    func registerDevice(command: CDVInvokedUrlCommand) {
        self.commandDelegate.run { [weak self] in
            guard let self else { return }
            
            Task {
                do {
                    if try await self.plugin?.requestAuthorisation() == true {
                        try await self.plugin?.subscribe(toTopic: .general)
                        try await self.plugin?.subscribe(toTopic: .appGeneral)
                        self.sendSuccess(callbackId: command.callbackId)
                    } else {
                        self.send(error: .notificationsPermissionsDeniedError, callbackId: command.callbackId)
                    }
                } catch let error as FirebaseMessagingErrors {
                    self.send(error: error == .requestPermissionsError ? .registrationPermissionsError : error, callbackId: command.callbackId)
                } catch {
                    self.send(error: .registrationError, callbackId: command.callbackId)
                }
            }
        }
    }
    
    @objc(getPendingNotifications:)
    func getPendingNotifications(command: CDVInvokedUrlCommand) {
        guard let clearFromDatabase = command.argument(at: 0) as? Bool else {
            return self.send(error: .obtainSilentNotificationsError, callbackId: command.callbackId)
        }
        
        self.commandDelegate.run { [weak self] in
            guard let self else { return }
            
            do {
                let notificationArray = try self.plugin?.getPendingNotifications()
                let result = notificationArray?.encode()    // this is done here as notifications might be clean with the next if statement.
                if clearFromDatabase, notificationArray?.isEmpty == false, let notificationArray {
                    do {
                        try self.plugin?.delete(pendingNotifications: notificationArray)
                    } catch {
                        throw FirebaseMessagingErrors.deleteNotificationsError
                    }
                }
                self.sendSuccess(result: result, callbackId: command.callbackId)
            } catch {
                self.send(error: error as? FirebaseMessagingErrors ?? .obtainSilentNotificationsError, callbackId: command.callbackId)
            }
        }
    }
    
    @objc(unregisterDevice:)
    func unregisterDevice(command: CDVInvokedUrlCommand) {
        self.commandDelegate.run { [weak self] in
            guard let self else { return }
            
            Task {
                do {
                    try await self.plugin?.deleteToken()
                } catch {
                    return self.send(error: .unregistrationDeleteTokenError, callbackId: command.callbackId)
                }
                
                do {
                    try await self.plugin?.unsubscribe(fromTopic: .general)
                    try await self.plugin?.unsubscribe(fromTopic: .appGeneral)
                    self.sendSuccess(callbackId: command.callbackId)
                } catch {
                    self.send(error: .unregistrationError, callbackId: command.callbackId)
                }
            }
        }
    }
    
    @objc(getToken:)
    func getToken(command: CDVInvokedUrlCommand) {
        self.commandDelegate.run { [weak self] in
            guard let self else { return }
            
            Task {
                guard let token = try? await self.plugin?.getToken()
                else { return self.send(error: .obtainingTokenError, callbackId: command.callbackId) }
                
                do {
                    try await self.plugin?.subscribe(toTopic: .general)
                    self.sendSuccess(result: token, callbackId: command.callbackId)
                } catch {
                    self.send(error: .subscriptionError, callbackId: command.callbackId)
                }
            }
        }
    }

    @objc(getAPNsToken:)
    func getAPNsToken(command: CDVInvokedUrlCommand) {
        self.commandDelegate.run { [weak self] in
            guard let self else { return }
            
            Task {
                guard let token = try? await self.plugin?.getToken(ofType: .apns)
                else { return self.send(error: .obtainingTokenError, callbackId: command.callbackId) }
                
                self.sendSuccess(result: token, callbackId: command.callbackId)
            }
        }
    }
    
    @objc(clearNotifications:)
    func clearNotifications(command: CDVInvokedUrlCommand) {
        self.commandDelegate.run { [weak self] in
            guard let self else { return }
            
            self.plugin?.clearNotifications()
            self.sendSuccess(callbackId: command.callbackId)
        }
    }
    
    @objc(sendLocalNotification:)
    func sendLocalNotification(command: CDVInvokedUrlCommand) {
        guard
            let badge = command.argument(at: 0) as? Int,
            let title = command.argument(at: 1) as? String,
            let body = command.argument(at: 2) as? String
        else { return self.send(error: .sendNotificationsError, callbackId: command.callbackId) }

        self.commandDelegate.run { [weak self] in
            guard let self else { return }
            
            Task {
                do {
                    try await self.plugin?.sendLocalNotification(title: title, body: body, badge: badge)
                    self.sendSuccess(callbackId: command.callbackId)
                } catch {
                    self.send(error: .sendNotificationsError, callbackId: command.callbackId)
                }
            }
        }
    }
    
    @objc(getBadge:)
    func getBadge(command: CDVInvokedUrlCommand) {
        DispatchQueue.main.async {
            guard let badgeNumber = self.plugin?.badgeNumber
            else { return self.send(error: .gettingBadgeNumberError, callbackId: command.callbackId)}
            
            self.sendSuccess(result: String(badgeNumber), callbackId: command.callbackId)
        }
    }
    
    @objc(setBadge:)
    func setBadge(command: CDVInvokedUrlCommand) {
        guard let newBadgeNumber = command.argument(at: 0) as? Int
        else { return self.send(error: .settingBadgeNumberError, callbackId: command.callbackId) }
        
        DispatchQueue.main.async {
            self.plugin?.badgeNumber = newBadgeNumber
            self.sendSuccess(callbackId: command.callbackId)
        }
    }
    
    @objc(subscribe:)
    func subscribe(command: CDVInvokedUrlCommand) {
        self.perform(
            operation: { [plugin] topicName in
                try await plugin?.subscribe(toTopic: .specific(name: topicName))
            },
            forTopic: command.argument(at: 0) as? String,
            with: command.callbackId,
            error: .subscriptionError
        )
    }
    
    @objc(unsubscribe:)
    func unsubscribe(command: CDVInvokedUrlCommand) {
        self.perform(
            operation: { [plugin] topicName in
                try await plugin?.unsubscribe(fromTopic: .specific(name: topicName))
            },
            forTopic: command.argument(at: 0) as? String,
            with: command.callbackId,
            error: .unsubscriptionError
        )
    }
    
    @objc(setDeliveryMetricsExportToBigQuery:)
    func setDeliveryMetricsExportToBigQuery(command: CDVInvokedUrlCommand) {
        guard
            let parameterDictionary = command.arguments.first as? [String: Any],
            let newValue = parameterDictionary["enable"] as? Bool
        else { return self.send(error: .setDeliveryMetricsExportToBigQueryError, callbackId: command.callbackId) }
        
        self.commandDelegate.run { [weak self] in
            guard let self else { return }
            
            self.firebaseAppDelegate.deliveryMetricsExportToBigQueryEnabled = newValue
            self.sendSuccess(callbackId: command.callbackId)
        }
    }
    
    @objc(deliveryMetricsExportToBigQueryEnabled:)
    func deliveryMetricsExportToBigQueryEnabled(command: CDVInvokedUrlCommand) {
        self.commandDelegate.run { [weak self] in
            guard let self else { return }
            
            self.sendSuccess(result: String(self.firebaseAppDelegate.deliveryMetricsExportToBigQueryEnabled), callbackId: command.callbackId)
        }
    }
}

private extension OSFirebaseCloudMessaging {
    func perform(operation: @escaping (String) async throws -> Void, forTopic topicName: String?, with callbackId: String, error fcmError: FirebaseMessagingErrors) {
        guard let topicName
        else { return self.send(error: fcmError, callbackId: callbackId) }
        
        self.commandDelegate.run { [weak self] in
            guard let self else { return }
            
            Task {
                do {
                    try await operation(topicName)
                    self.sendSuccess(callbackId: callbackId)
                } catch {
                    self.send(error: fcmError, callbackId: callbackId)
                }
            }
        }
    }

    func sendSuccess(result: String? = nil, callbackId: String) {
        let pluginResult = CDVPluginResult(status: .ok, messageAs: result)
        self.commandDelegate.send(pluginResult, callbackId: callbackId)
    }
    
    func send(error: FirebaseMessagingErrors, callbackId: String) {
        let pluginResult = CDVPluginResult(status: .error, messageAs: [
            "code": "OS-PLUG-FCMS-\(String(format: "%04d", error.rawValue))",
            "message": error.description
        ])
        self.commandDelegate.send(pluginResult, callbackId: callbackId)
    }
    
    func trigger(event: String, data: String) {
        let js = "cordova.plugins.OSFirebaseCloudMessaging.fireEvent('\(event)', \(data))"
        
        if self.deviceReady {
            self.commandDelegate.evalJs(js)
        } else {
            self.eventQueue = (self.eventQueue ?? []) + [js]
        }
    }
}

// MARK: - OSFirebaseMessagingLib's FirebaseMessagingEventProtocol Methods
extension OSFirebaseCloudMessaging: FirebaseMessagingEventProtocol {
    func event(_ event: FirebaseEventType, data: String) {
        let eventName: CustomStringConvertible = switch event {
        case .click(type: let type):
            type
        case .trigger(notification: let notification):
            notification
        @unknown default:
            preconditionFailure("Not supposed to get here")
        }
        
        self.trigger(event: eventName.description, data: data)
    }
}
