package com.outsystems.payments

import android.app.Activity
import android.content.Intent
import com.google.gson.Gson
import com.outsystems.plugins.payments.controller.OSPMTGooglePayManager
import com.outsystems.plugins.payments.controller.OSPMTGooglePlayHelper
import com.outsystems.plugins.payments.controller.OSPMTController
import com.outsystems.plugins.payments.model.OSPMTError
import com.outsystems.plugins.payments.model.PaymentConfigurationInfo
import com.outsystems.plugins.payments.model.PaymentDetails
import com.outsystems.plugins.payments.model.Tokenization
import org.apache.cordova.CallbackContext
import org.apache.cordova.CordovaInterface
import org.apache.cordova.CordovaPlugin
import org.apache.cordova.CordovaWebView
import org.apache.cordova.PluginResult
import org.apache.cordova.PluginResult.Status
import org.json.JSONArray
import org.json.JSONObject

class OSPayments : CordovaPlugin() {

    var callbackContext: CallbackContext? = null
    private lateinit var googlePayManager: OSPMTGooglePayManager
    private lateinit var paymentsController: OSPMTController
    private lateinit var googlePlayHelper: OSPMTGooglePlayHelper

    //to delete
    private var paymentDetails: PaymentDetails? = null

    val gson by lazy { Gson() }

    companion object {
        private const val ERROR_FORMAT_PREFIX = "OS-PLUG-PAYM-"
        private const val MERCHANT_NAME = "merchant_name"
        private const val MERCHANT_COUNTRY_CODE = "merchant_country_code"
        private const val PAYMENT_ALLOWED_NETWORKS = "payment_allowed_networks"
        private const val PAYMENT_SUPPORTED_CAPABILITIES = "payment_supported_capabilities"
        private const val PAYMENT_SUPPORTED_CARD_COUNTRIES = "payment_supported_card_countries"
        private const val SHIPPING_SUPPORTED_CONTACTS = "shipping_supported_contacts"
        private const val SHIPPING_COUNTRY_CODES = "shipping_country_codes"
        private const val BILLING_SUPPORTED_CONTACTS = "billing_supported_contacts"
        private const val GATEWAY = "gateway"
        private const val BACKEND_URL = "backend_url"
        private const val GATEWAY_MERCHANT_ID = "gateway_merchant_id"
        private const val STRIPE_VERSION = "stripe_version"
        private const val STRIPE_PUB_KEY = "stripe_pub_key"
    }

    override fun initialize(cordova: CordovaInterface, webView: CordovaWebView) {
        super.initialize(cordova, webView)
        googlePayManager = OSPMTGooglePayManager(cordova.activity)
        googlePlayHelper = OSPMTGooglePlayHelper()
        paymentsController = OSPMTController(googlePayManager, buildPaymentConfigurationInfo(cordova.activity), googlePlayHelper)
    }

    override fun execute(action: String, args: JSONArray, callbackContext: CallbackContext): Boolean {
        this.callbackContext = callbackContext
        when (action) {
            "setupConfiguration" -> {
                setupConfiguration(args)
            }
            "checkWalletSetup" -> {
                checkWalletSetup()
            }
            "setDetails" -> {
                setDetailsAndTriggerPayment(args)
            }
            else -> return false
        }
        return true
    }

    private fun setupConfiguration(args: JSONArray) {
        paymentsController.setupConfiguration(cordova.activity, args.get(0).toString(),
            {
                sendPluginResult(it, null)
            },
            {
                sendPluginResult(null, Pair(formatErrorCode(it.code), it.description))
            }
        )
    }

    private fun checkWalletSetup(){
        paymentsController.verifyIfWalletIsSetup(cordova.activity,
            {
                sendPluginResult(it, null)
            }, {
                sendPluginResult(null, Pair(formatErrorCode(it.code), it.description))
            }
        )
    }

    private fun setDetailsAndTriggerPayment(args: JSONArray){
        cordova.setActivityResultCallback(this)

        paymentDetails = buildPaymentDetails(args)

        if(paymentDetails != null){
            paymentsController.setDetailsAndTriggerPayment(cordova.activity, paymentDetails!!, args.getString(1))
        }
        else{
            sendPluginResult(null, Pair(formatErrorCode(OSPMTError.INVALID_PAYMENT_DETAILS.code), OSPMTError.INVALID_PAYMENT_DETAILS.description))
        }
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, intent: Intent?) {
        super.onActivityResult(requestCode, resultCode, intent)
        if (intent != null) {
            paymentsController.handleActivityResult(requestCode, resultCode, intent,
                { paymentResponse ->
                    sendPluginResult(paymentResponse, null)
                },
                { error ->
                    sendPluginResult(null, Pair(formatErrorCode(error.code), error.description))
                })
        }
    }

    override fun onRequestPermissionResult(requestCode: Int,
                                           permissions: Array<String>,
                                           grantResults: IntArray) {
        // Does nothing. These permissions are not required on Android.
    }

    override fun onResume(multitasking: Boolean) {
        // Not used in this project.
    }

    private fun formatErrorCode(code: Int): String {
        return ERROR_FORMAT_PREFIX + code.toString().padStart(4, '0')
    }

    private fun buildPaymentConfigurationInfo(activity: Activity) : PaymentConfigurationInfo{

        val shippingContacts = activity.getString(getStringResourceId(activity, SHIPPING_SUPPORTED_CONTACTS)).split(",")
        val shippingCountries = activity.getString(getStringResourceId(activity, SHIPPING_COUNTRY_CODES)).split(",")
        val billingContacts = activity.getString(getStringResourceId(activity, BILLING_SUPPORTED_CONTACTS)).split(",")

        return PaymentConfigurationInfo(
            activity.getString(getStringResourceId(activity, MERCHANT_NAME)),
            activity.getString(getStringResourceId(activity, MERCHANT_COUNTRY_CODE)),
            activity.getString(getStringResourceId(activity, PAYMENT_ALLOWED_NETWORKS)).split(","),
            activity.getString(getStringResourceId(activity, PAYMENT_SUPPORTED_CAPABILITIES)).split(","),
            activity.getString(getStringResourceId(activity, PAYMENT_SUPPORTED_CARD_COUNTRIES)).split(","),
            if(shippingContacts.isNotEmpty() && shippingContacts[0].isNotEmpty()) shippingContacts else listOf(),
            if(shippingCountries.isNotEmpty() && shippingCountries[0].isNotEmpty()) shippingCountries else listOf(),
            if(billingContacts.isNotEmpty() && billingContacts[0].isNotEmpty()) billingContacts else listOf(),
            Tokenization(
                activity.getString(getStringResourceId(activity, GATEWAY)),
                activity.getString(getStringResourceId(activity, BACKEND_URL)),
                activity.getString(getStringResourceId(activity, GATEWAY_MERCHANT_ID)),
                activity.getString(getStringResourceId(activity, STRIPE_VERSION)),
                activity.getString(getStringResourceId(activity, STRIPE_PUB_KEY))
            )
        )
    }

    private fun buildPaymentDetails(args: JSONArray) : PaymentDetails? {
        return try {
            gson.fromJson(args.getString(0), PaymentDetails::class.java)
        } catch (_: Exception){
            null
        }
    }

    private fun getStringResourceId(activity: Activity, typeAndName: String): Int {
        return activity.resources.getIdentifier(typeAndName, "string", activity.packageName)
    }

    private fun <T> sendPluginResult(resultVariable: T, error: Pair<String, String>?) {
        var pluginResult: PluginResult?
        resultVariable?.let {
            pluginResult = PluginResult(Status.OK, resultVariable.toString())
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