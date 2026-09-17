package com.outsystems.plugins.sociallogins

import android.content.Intent
import com.google.gson.Gson
import com.outsystems.plugins.sociallogins.apple.AppleHelper
import com.outsystems.plugins.sociallogins.apple.SocialLoginsAppleController
import com.outsystems.plugins.sociallogins.facebook.FacebookHelper
import com.outsystems.plugins.sociallogins.facebook.SocialLoginsFacebookController
import com.outsystems.plugins.sociallogins.google.GoogleHelper
import com.outsystems.plugins.sociallogins.google.SocialLoginsGoogleController
import com.outsystems.plugins.sociallogins.linkedin.SocialLoginsLinkedinController
import org.apache.cordova.CallbackContext
import org.apache.cordova.CordovaInterface
import org.apache.cordova.CordovaPlugin
import org.apache.cordova.CordovaWebView
import org.apache.cordova.PluginResult
import org.apache.cordova.PluginResult.Status
import org.json.JSONArray
import org.json.JSONException
import org.json.JSONObject

class OSSocialLogins : CordovaPlugin() {
    private val gson by lazy { Gson() }
    private var socialLoginController: SocialLoginsController? = null
    var callbackContext: CallbackContext? = null

    private var appleController: SocialLoginsAppleController? = null
    private var googleController: SocialLoginsGoogleController? = null
    private var linkedinController: SocialLoginsLinkedinController? = null
    private var facebookController: SocialLoginsFacebookController? = null

    companion object {
        private const val ERROR_FORMAT_PREFIX = "OS-PLUG-SOCI-"
    }

    private var delegate  = object : SocialLoginsInterface {
        override fun callbackError(error: SocialLoginError) {
            sendPluginResult(null, Pair(formatErrorCode(error.code), error.message))
        }
        override fun callbackUserInfo(result: UserInfo) {
            val pluginResponseJson = gson.toJson(result)
            sendPluginResult(pluginResponseJson, null)
        }
    }

    override fun initialize(cordova: CordovaInterface, webView: CordovaWebView) {
        super.initialize(cordova, webView)

        initializeApple()
        initializeGoogle()
        initializeLinkedIn()
        initializeFacebook()

        socialLoginController = SocialLoginsController(
            appleController,
            googleController,
            linkedinController,
            facebookController)
    }

    override fun onRequestPermissionResult(
        requestCode: Int,
        permissions: Array<String>,
        grantResults: IntArray
    ) {
        // Does nothing for this plugin
    }

    override fun onResume(multitasking: Boolean) {
        // Not used in this project.
    }

    private fun initializeApple() {
        if(appleController != null) return
        val appleHelperInterface = AppleHelper()
        appleController = SocialLoginsAppleController(delegate, appleHelperInterface)
    }

    private fun initializeGoogle() {
        val googleHelperInterface = GoogleHelper()
        googleController = SocialLoginsGoogleController(delegate, cordova.context, googleHelperInterface)
    }

    private fun initializeLinkedIn() {
        linkedinController = SocialLoginsLinkedinController(delegate)
    }

    private fun initializeFacebook() {
        val facebookHelper = FacebookHelper(cordova.activity)
        facebookController = SocialLoginsFacebookController(delegate, facebookHelper)
    }

    override fun execute(
        action: String,
        args: JSONArray,
        callbackContext: CallbackContext
    ): Boolean {
        this.callbackContext = callbackContext

        when (action) {
            "loginApple" -> {
                doLoginApple(args)
            }
            "loginGoogle" -> {
                doLoginGoogle()
            }
            "loginFacebook" -> {
                doLoginFacebook()
            }
            "loginLinkedIn" -> {
                doLoginLinkedIn(args)
            }
        }
        return true
    }

    private fun doLoginLinkedIn(args : JSONArray) {
        var state: String
        var clientId: String
        var redirectUri: String

        try {
            state = args.get(0).toString()
            clientId = args.get(1).toString()
            redirectUri = args.get(2).toString()

            if(clientId.isEmpty() || redirectUri.isEmpty()){
                sendPluginResult(null, Pair(formatErrorCode(SocialLoginError.MISSING_INPUT_PARAMETERS_ERROR.code), SocialLoginError.MISSING_INPUT_PARAMETERS_ERROR.message))
            }
            else{
                cordova.setActivityResultCallback(this)
                socialLoginController?.doLoginLinkedin(state, clientId, redirectUri, cordova.activity)
            }
        }catch (_: JSONException){
            sendPluginResult(null, Pair(formatErrorCode(SocialLoginError.MISSING_INPUT_PARAMETERS_ERROR.code), SocialLoginError.MISSING_INPUT_PARAMETERS_ERROR.message))
        }catch (_: Exception){
            sendPluginResult(null, Pair(formatErrorCode(SocialLoginError.APPLE_SIGN_IN_GENERAL_ERROR.code), SocialLoginError.APPLE_SIGN_IN_GENERAL_ERROR.message))
        }

    }
    private fun doLoginApple(args : JSONArray) {

        var state: String
        var clientId: String
        var redirectUri: String

        try {
            state = args.get(0).toString()
            clientId = args.get(1).toString()
            redirectUri = args.get(2).toString()

            if(clientId.isEmpty() || redirectUri.isEmpty()){
                sendPluginResult(null, Pair(formatErrorCode(SocialLoginError.MISSING_INPUT_PARAMETERS_ERROR.code), SocialLoginError.MISSING_INPUT_PARAMETERS_ERROR.message))
            }
            else{
                cordova.setActivityResultCallback(this)
                socialLoginController?.doLoginApple(state, clientId, redirectUri, cordova.activity)
            }
        }catch (_: JSONException){
            sendPluginResult(null, Pair(formatErrorCode(SocialLoginError.MISSING_INPUT_PARAMETERS_ERROR.code), SocialLoginError.MISSING_INPUT_PARAMETERS_ERROR.message))
        }catch (_: Exception){
            sendPluginResult(null, Pair(formatErrorCode(SocialLoginError.APPLE_SIGN_IN_GENERAL_ERROR.code), SocialLoginError.APPLE_SIGN_IN_GENERAL_ERROR.message))
        }

    }
    private fun doLoginGoogle() {
        cordova.setActivityResultCallback(this)
        socialLoginController?.doLoginGoogle(cordova.activity)
    }
    private fun doLoginFacebook() {
        cordova.setActivityResultCallback(this)
        socialLoginController?.doLoginFacebook()
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, intent: Intent?) {
        super.onActivityResult(requestCode, resultCode, intent)
        socialLoginController?.handleActivityResult(requestCode, resultCode, intent)
    }

    private fun formatErrorCode(code: Int): String {
        return ERROR_FORMAT_PREFIX + code.toString().padStart(4, '0')
    }

    private fun sendPluginResult(result: String?, error: Pair<String, String>?) {
        var pluginResult: PluginResult?
        result?.let {
            pluginResult = PluginResult(Status.OK, result)
            this.callbackContext?.sendPluginResult(pluginResult)
            return
        }
        val jsonResult = JSONObject()
        jsonResult.put("code", error?.first)
        jsonResult.put("message", error?.second ?: "No Results")
        pluginResult = PluginResult(Status.ERROR, jsonResult)
        this.callbackContext?.sendPluginResult(pluginResult)
    }

}