import Foundation
import OSSocialLoginsLib

@objc(OSSocialLogins)
class OSSocialLogins: CDVPlugin {
    
    var plugin: SocialLoginsController?
    var callbackId:String=""
    
    override func pluginInitialize() {
        let appleController = SocialLoginsAppleController(delegate:self, rootViewController: self.viewController)
        let googleController = SocialLoginsGoogleController(delegate:self, rootViewController: self.viewController)
        let facebookController = SocialLoginsFacebookController(delegate: self, rootViewController: self.viewController)
        let linkedInController = SocialLoginsLinkedInController(delegate: self, rootViewController: self.viewController)
        plugin = SocialLoginsController(appleController: appleController, 
                                        googleController: googleController,
                                        facebookController: facebookController,
                                        linkedInController: linkedInController)
    }
    
    @objc(loginApple:)
    func loginApple(command: CDVInvokedUrlCommand) {
        self.callbackId = command.callbackId
        self.plugin?.loginApple()
    }

    @objc(loginGoogle:)
    func loginGoogle(command: CDVInvokedUrlCommand) {
        self.callbackId = command.callbackId
        self.plugin?.loginGoogle()
    }

    @objc(loginFacebook:)
    func loginFacebook(command: CDVInvokedUrlCommand) {
        self.callbackId = command.callbackId
        self.plugin?.loginFacebook()
    }

    @objc(loginLinkedIn:)
    func loginLinkedIn(command: CDVInvokedUrlCommand) {
        self.callbackId = command.callbackId

        guard 
            let state = command.arguments[0] as? String,
            let clientID = command.arguments[1] as? String,
            let redirectURI = command.arguments[2] as? String
        else {
            self.sendResult(result: "", error:SocialLoginsErrors.linkedInConfigurationNotValidError as NSError, callBackID: self.callbackId)
            return
        }
        
        self.plugin?.loginLinkedIn(state: state, clientID: clientID, redirectURI: redirectURI)
    }
    
    private func sendResult(result: String?, error: NSError?, callBackID: String) {
        var pluginResult = CDVPluginResult(status: CDVCommandStatus_ERROR)

        if let error = error, !error.localizedDescription.isEmpty {
            let errorCode = "OS-PLUG-SOCI-\(String(format: "%04d", error.code))"
            let errorMessage = error.localizedDescription
            let errorDict = ["code": errorCode, "message": errorMessage]
            pluginResult = CDVPluginResult(status: CDVCommandStatus_ERROR, messageAs: errorDict);
        } else if let result = result {
            pluginResult = result.isEmpty ? CDVPluginResult(status: CDVCommandStatus_OK) : CDVPluginResult(status: CDVCommandStatus_OK, messageAs: result)
        }

        self.commandDelegate.send(pluginResult, callbackId: callBackID);
    }
}

extension OSSocialLogins: SocialLoginsProtocol {
    func callBackUserInfo(result: UserInfo?, error: SocialLoginsErrors?) {
        if let error = error {
            self.sendResult(result: nil, error:error as NSError, callBackID: self.callbackId)
        } else {
            let finalResult = result?.encode()
            self.sendResult(result: finalResult, error:nil , callBackID: self.callbackId)
        }
    }
}
