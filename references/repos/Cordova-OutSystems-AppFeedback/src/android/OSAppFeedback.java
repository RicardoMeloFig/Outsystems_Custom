package com.outsystems.plugins.appfeedback;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.os.Bundle;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.WebView;
import android.widget.LinearLayout;
import android.widget.RelativeLayout;

import androidx.localbroadcastmanager.content.LocalBroadcastManager;

import com.outsystems.plugins.broadcaster.interfaces.Event;

import org.apache.cordova.CallbackContext;
import org.apache.cordova.CordovaActivity;
import org.apache.cordova.CordovaPlugin;
import org.apache.cordova.engine.SystemWebViewEngine;
import org.json.JSONArray;
import org.json.JSONException;

import java.util.Map;

public class OSAppFeedback extends CordovaPlugin {

    private static final String DEFAULT_HOSTNAME = "DefaultHostname";
    private static final String DEFAULT_HANDLER = "DefaultAppFeedbackHandler";

    // These constants match the ones defined in the Broadcaster plugin
    // They are intended to use for MABS 6 only
    // If any of these changes on Broadcaster plugin it should be reflected here
    private static final String GESTURE_EVENT = "gestureEvent";
    private static final String GESTURE_TYPE = "gestureType";
    private static final String GESTURE_TAP = "gestureTap";
    private static final String GESTURE_LONG_PRESS = "gestureLongPress";
    private static final String GESTURE_NUMBER_FINGERS = "gestureNumberFingers";
    private static final String GESTURE_ONE_FINGER = "1";
    private static final String GESTURE_TWO_FINGERS = "2";
    private static final String GESTURE_THREE_FINGERS = "3";

    private OSAppFeedbackListener appFeedbackListener;
    private ViewGroup mainViewGroup;
    private ViewGroup ectViewGroup;
    private BroadcastReceiver broadcastReceiver;
    private boolean inBackground;
    private String defaultHostname;

    protected void setMainViewGroup(ViewGroup mainViewGroup) {
        this.mainViewGroup = mainViewGroup;
    }

    protected void setEctViewGroup(ViewGroup ectViewGroup) {
        this.ectViewGroup = ectViewGroup;
    }

    @SuppressLint("ResourceType")
    @Override
    protected void pluginInitialize() {

        this.defaultHostname = preferences.getString(DEFAULT_HOSTNAME, null);
        final boolean defautHandler = preferences.getBoolean(DEFAULT_HANDLER, true);

        cordova.getActivity().runOnUiThread(() -> {

            final CordovaActivity cordovaActivity = (CordovaActivity) cordova.getActivity();

            ViewGroup mainViewGroup = (ViewGroup) webView.getView().getParent();
            ViewGroup rootView = (ViewGroup) mainViewGroup.getParent();

            if (rootView instanceof LinearLayout) {
                LinearLayout linearLayout = (LinearLayout) rootView;
                linearLayout.setOrientation(LinearLayout.VERTICAL);
            }

            RelativeLayout ectViewGroup = new RelativeLayout(cordovaActivity);
            RelativeLayout.LayoutParams params = new RelativeLayout.LayoutParams(RelativeLayout.LayoutParams.MATCH_PARENT, RelativeLayout.LayoutParams.MATCH_PARENT);
            ectViewGroup.setLayoutParams(params);
            ectViewGroup.setId(2001);
            ectViewGroup.setFitsSystemWindows(true);
            ectViewGroup.setVisibility(View.GONE);

            rootView.addView(ectViewGroup);
            rootView.invalidate();

            setMainViewGroup(mainViewGroup);
            setEctViewGroup(ectViewGroup);

            if (defautHandler) {
                registerGestureHandler();
            }
        });
    }

    @Override
    public void onResume(boolean multitasking) {
        super.onResume(multitasking);
        inBackground = false;
    }

    @Override
    public void onStart() {
        super.onStart();
        final Activity activity = this.cordova.getActivity();
        LocalBroadcastManager.getInstance(activity.getApplicationContext()).registerReceiver(this.broadcastReceiver, new IntentFilter(GESTURE_EVENT));
    }

    @Override
    public void onPause(boolean multitasking) {
        super.onPause(multitasking);
        inBackground = true;
    }

    @Override
    public void onStop() {
        final Activity activity = this.cordova.getActivity();
        LocalBroadcastManager.getInstance(activity.getApplicationContext()).unregisterReceiver(this.broadcastReceiver);
        super.onStop();
    }

    @Override
    public boolean execute(String action, JSONArray args, final CallbackContext callbackContext) throws JSONException {
        if (inBackground) {
            callbackContext.error("Can't open ECT while in background.");
            return false;
        }

        switch (action) {
            case "deviceready":
                if (defaultHostname == null || defaultHostname.isEmpty()) {
                    defaultHostname = args.getString(0);
                }
                handleDeviceReady(defaultHostname);
                break;
            case "isAvailable":
                handleIsECTAvailable(callbackContext);
                break;
            case "openECT":
                handleOpenECT(callbackContext);
                break;
            default:
                callbackContext.error("Invalid plugin action.");
                return false;
        }
        return true;
    }

    private void handleDeviceReady(String hostname) {
        SystemWebViewEngine webViewEngine = (SystemWebViewEngine) webView.getEngine();
        WebView currentWebView = (WebView) webViewEngine.getView();

        appFeedbackListener = new OSAppFeedbackListener(cordova.getActivity(), mainViewGroup, ectViewGroup, currentWebView, hostname);
        cordova.getActivity().runOnUiThread(() -> appFeedbackListener.handleDeviceReady());
    }

    private void handleIsECTAvailable(final CallbackContext callbackContext) {
        cordova.getActivity().runOnUiThread(() -> appFeedbackListener.handleECTAvailable(result -> {
            if (result) {
                callbackContext.success(1);
            } else {
                callbackContext.error("App Feedback is not available.");
            }
        }));
    }

    private void handleOpenECT(final CallbackContext callbackContext) {
        cordova.getActivity().runOnUiThread(() -> appFeedbackListener.handleOpenECT(result -> {
            if (result) {
                callbackContext.success(1);
            } else {
                callbackContext.error("App Feedback is not available.");
            }
        }));
    }

    private void registerGestureHandler() {
        final CordovaActivity cordovaActivity = (CordovaActivity) cordova.getActivity();

        this.broadcastReceiver = new BroadcastReceiver() {
            @Override
            public void onReceive(Context context, Intent intent) {
                Bundle extras = intent.getExtras();
                if (extras != null) {
                    Event gestureEvent = extras.getParcelable(GESTURE_EVENT);
                    if (gestureEvent != null) {
                        Map<String, String> eventData = gestureEvent.getData();
                        if (eventData != null) {
                            if (GESTURE_LONG_PRESS.equals(eventData.get(GESTURE_TYPE)) &&
                                    GESTURE_TWO_FINGERS.equals(eventData.get(GESTURE_NUMBER_FINGERS))) {
                                cordovaActivity.runOnUiThread(() -> appFeedbackListener.handleECTAvailable(result -> {
                                    if (result) {
                                        appFeedbackListener.handleOpenECT(null);
                                    }
                                }));
                            }
                        }
                    }
                }
            }
        };
    }
}
