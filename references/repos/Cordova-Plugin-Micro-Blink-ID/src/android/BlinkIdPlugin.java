package com.os.mobile.blinkid;

import android.content.Intent;
import android.os.Parcelable;
import android.widget.Toast;

import com.microblink.activity.ScanActivity;
import com.microblink.activity.ScanCard;
import com.microblink.activity.ShowOcrResultMode;
import com.microblink.recognizers.BaseRecognitionResult;
import com.microblink.recognizers.ocr.mrtd.MRTDRecognitionResult;
import com.microblink.recognizers.ocr.mrtd.MRTDRecognizerSettings;
import com.microblink.recognizers.settings.GenericRecognizerSettings;
import com.microblink.recognizers.settings.RecognizerSettings;
import com.microblink.util.Log;
import com.microblink.util.RecognizerCompatibility;
import com.microblink.util.RecognizerCompatibilityStatus;

import org.apache.cordova.CallbackContext;
import org.apache.cordova.CordovaPlugin;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

/**
 * Created by vitoroliveira on 06/11/15.
 */
public class BlinkIdPlugin extends CordovaPlugin {

    /* Request Code received on Activity Result  */
    public static final int MY_PHOTOPAY_REQUEST_CODE = 0x101;

    /* Cordova Plugin - Action to Read Cards */
    public static final String ACTION_READ_CARD_ID = "readCardId";

    private CallbackContext callbackContext;

    @Override
    public boolean execute(String action, JSONArray args, CallbackContext callbackContext) throws JSONException {
        this.callbackContext = callbackContext;
        if (ACTION_READ_CARD_ID.equals(action)) {
            readCardId(callbackContext, args);
        }
        return true;
    }

    /**
     * Method to launch Card ID Reader
     *
     * @param callbackContext
     * @param args
     * @throws JSONException
     */
    private void readCardId (CallbackContext callbackContext, JSONArray args) throws JSONException {

        // check if BlinkID is supported on the device
        RecognizerCompatibilityStatus supportStatus = RecognizerCompatibility.getRecognizerCompatibilityStatus(cordova.getActivity());
        if(supportStatus != RecognizerCompatibilityStatus.RECOGNIZER_SUPPORTED) {
            callbackContext.error("BlinkID is not supported! Reason: " + supportStatus.name());
            return;
        }

        if(args != null) {
            /* Get the license key from cordova */
            String licenseKey = args.getString(0);

            if(licenseKey == null){
                callbackContext.error("Is mandatory a license key to use the this plugin");
                return;
            }

            /* Launch the Activity to read the card id */
            this.cordova.startActivityForResult((CordovaPlugin) this, buildMrtdIntent(licenseKey), MY_PHOTOPAY_REQUEST_CODE);

        }
    }

    @Override
    public void onActivityResult(int requestCode, int resultCode, Intent intent) {
        super.onActivityResult(requestCode, resultCode, intent);

        if (requestCode == MY_PHOTOPAY_REQUEST_CODE) {

            // make sure PhotoPay activity returned result
            if (resultCode == ScanActivity.RESULT_OK && intent != null) {

                // depending on settings, we may have multiple scan results.
                // we first need to obtain list of recognition results
                Parcelable[] multiData = intent.getParcelableArrayExtra(ScanActivity.EXTRAS_RECOGNITION_RESULT_LIST);

                if (multiData != null) {

                    Log.i(this, "Data count: " + multiData.length);
                    int i = 1;

                    for (Parcelable parc : multiData) {
                        Log.i(this, "Data #" + Integer.valueOf(i++).toString());

                        // each element in multiData is actually class derived from BaseRecognitionResult
                        // so it is always safe to cast
                        // Moreover, as is specified in README file, you can use instanceof operator
                        // to determine the actual type of result. Here we will simply pass
                        // the result list to ResultActivity and there we will explain
                        // how to retrieve data from result.

                        BaseRecognitionResult rd = (BaseRecognitionResult) parc;

                        /* Create JSON to send as a result to the call of the Plugin  */
                        if(rd != null){
                            JSONObject jsonObject = new JSONObject();
                            try {
                                jsonObject.put("isParsed", ((MRTDRecognitionResult) rd).isMRZParsed());
                                jsonObject.put("issuer", ((MRTDRecognitionResult) rd).getIssuer());
                                jsonObject.put("documentNumber", ((MRTDRecognitionResult) rd).getDocumentNumber());
                                jsonObject.put("documentCode", ((MRTDRecognitionResult) rd).getDocumentCode());
                                jsonObject.put("dateOfExpiry", ((MRTDRecognitionResult) rd).getDateOfExpiry());
                                jsonObject.put("primaryId", ((MRTDRecognitionResult) rd).getPrimaryId());
                                jsonObject.put("secondaryId", ((MRTDRecognitionResult) rd).getSecondaryId());
                                jsonObject.put("dateOfBirth", ((MRTDRecognitionResult) rd).getDateOfBirth());
                                jsonObject.put("nationality", ((MRTDRecognitionResult) rd).getNationality());
                                jsonObject.put("sex", ((MRTDRecognitionResult) rd).getSex());
                                jsonObject.put("opt1", ((MRTDRecognitionResult) rd).getOpt1());
                                jsonObject.put("opt2", ((MRTDRecognitionResult) rd).getOpt2());
                                jsonObject.put("mrzText", ((MRTDRecognitionResult) rd).getMRZText());

                                this.callbackContext.success(jsonObject.toString());
                               // break;
                            } catch (JSONException e) {
                                Log.e("MicroBlink", e.toString());
                            }
                        }

                    }
                } else {
                    Log.e(this, "Unable to retrieve list of recognition data!");
                }

                // set intent's component to ResultActivity and pass its contents
                // to ResultActivity. ResultActivity will show how to extract
                // data from result.

                //   intent.setComponent(new ComponentName(this, ResultActivity.class));
                //   startActivity(intent);
            } else {
                // if PhotoPay activity did not return result, user has probably
                // pressed Back button and cancelled scanning
                Toast.makeText(cordova.getActivity(), "Scan cancelled!", Toast.LENGTH_SHORT).show();
            }
        }
    }

    private Intent buildMrtdIntent(String licenseKey) {
        // prepare settings for Machine Readable Travel Document (MRTD) recognizer
        MRTDRecognizerSettings mrtd = new MRTDRecognizerSettings();

        // build a scan intent by adding intent extras common to all other recognizers
        // when scanning ID documents, we will use ScanCard activity which has more suitable UI for scanning ID documents
        //   return new ListElement("ID document", buildIntent(new RecognizerSettings[]{mrtd}, ScanCard.class, null));

        return buildIntent(new RecognizerSettings[]{mrtd}, ScanCard.class, null, licenseKey);
    }

    /**
     * This method will build scan intent for PhotoPay. Method needs array of recognizer settings
     * to know which recognizers to enable, activity to which intent will be sent and optionally
     * an intent for HelpActivity that will be used if user taps the Help button on scan activity.
     */
    private Intent buildIntent(RecognizerSettings[] settArray, Class<?> target, Intent helpIntent, String licenseKey) {
        // first create intent for given activity
        final Intent intent = new Intent(cordova.getActivity(), target);

        // optionally, if you want the beep sound to be played after a scan
        // add a sound resource id as EXTRAS_BEEP_RESOURCE extra
        //TODO
      //  intent.putExtra(ScanActivity.EXTRAS_BEEP_RESOURCE, R.raw.beep);

        // if we have help intent, we can pass it to scan activity so it can invoke
        // it if user taps the help button. If we do not set the help intent,
        // scan activity will hide the help button.
        if (helpIntent != null) {
            intent.putExtra(ScanActivity.EXTRAS_HELP_INTENT, helpIntent);
        }

        // now add array with recognizer settings so that scan activity will know
        // what do you want to scan. Setting recognizer settings array is mandatory.
        intent.putExtra(ScanActivity.EXTRAS_RECOGNIZER_SETTINGS_ARRAY, settArray);

        // generic recognizer settings are optional and give you the ability to
        // define settings that are valid for all recognizers.
        GenericRecognizerSettings genSett = new GenericRecognizerSettings();

        // with setNumMsBeforeTimeout you can define number of miliseconds that must pass
        // after first partial scan result has arrived before scan activity triggers a timeout.
        // Timeout is good for preventing infinitely long scanning experience when user attempts
        // to scan damaged or unsupported slip. After timeout, scan activity will return only
        // data that was read successfully. This might be incomplete data.
        genSett.setNumMsBeforeTimeout(10000);

        // If you add more recognizers to recognizer settings array, you can choose whether you
        // want to have the ability to obtain multiple scan results from same video frame. For example,
        // if both payment slip and payment barcode are visible on a single frame, by setting
        // setAllowMultipleScanResultsOnSingleImage to true you can obtain both scan results
        // from barcode and slip. If this is false (default), you will get the first valid result
        // (i.e. first result that contains all required data). Having this option turned off
        // creates better and faster user experience.
//        genSett.setAllowMultipleScanResultsOnSingleImage(true);

        // once generic settings are prepared, you can set them with EXTRAS_GENERIC_SETTINGS extra
        // Setting generic settings is optional.
        intent.putExtra(ScanActivity.EXTRAS_GENERIC_SETTINGS, genSett);

        // In order for scanning to work, you must enter a valid licence key. Without licence key,
        // scanning will not work. Licence key is bound the the package name of your app, so when
        // obtaining your licence key from Microblink make sure you give us the correct package name
        // of your app.
        // Licence key also defines which recognizers are enabled and which are not. Since the licence
        // key validation is performed on image processing thread in native code, all enabled recognizers
        // that are disallowed by licence key will be turned off without any error and information
        // about turning them off will be logged to ADB logcat.
        intent.putExtra(ScanActivity.EXTRAS_LICENSE_KEY, licenseKey);

        // If you want, you can disable drawing of OCR results on scan activity. Drawing OCR results can be visually
        // appealing and might entertain the user while waiting for scan to complete, but might introduce a small
        // performance penalty.
        // intent.putExtra(ScanActivity.EXTRAS_SHOW_OCR_RESULT, false);

        // If you want you can have scan activity display the focus rectangle whenever camera
        // attempts to focus, similarly to various camera app's touch to focus effect.
        // By default this is off, and you can turn this on by setting EXTRAS_SHOW_FOCUS_RECTANGLE
        // extra to true.
        intent.putExtra(ScanActivity.EXTRAS_SHOW_FOCUS_RECTANGLE, true);

        // If you want, you can enable the pinch to zoom feature of scan activity.
        // By enabling this you allow the user to use the pinch gesture to zoom the camera.
        // By default this is off and can be enabled by setting EXTRAS_ALLOW_PINCH_TO_ZOOM extra to true.
        intent.putExtra(ScanActivity.EXTRAS_ALLOW_PINCH_TO_ZOOM, true);

        // Enable showing of OCR results as animated dots. This does not have effect if non-OCR recognizer like
        // barcode recognizer is active.
        intent.putExtra(ScanActivity.EXTRAS_SHOW_OCR_RESULT_MODE, (Parcelable) ShowOcrResultMode.ANIMATED_DOTS);

        return intent;
    }
}
