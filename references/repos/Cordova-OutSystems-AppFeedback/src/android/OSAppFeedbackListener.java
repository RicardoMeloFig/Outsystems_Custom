package com.outsystems.plugins.appfeedback;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Context;
import android.content.SharedPreferences;
import android.net.ConnectivityManager;
import android.net.NetworkInfo;
import android.view.ViewGroup;
import android.webkit.WebView;

import androidx.appcompat.app.AppCompatActivity;

import com.outsystems.android.mobileect.MobileECTController;
import com.outsystems.android.mobileect.api.interfaces.OSECTProviderAPIHandler;
import com.outsystems.android.mobileect.interfaces.OSECTContainerListener;

public class OSAppFeedbackListener implements OSECTContainerListener {

    private final String SHAREDPREF_NAME = "com.outsystems.plugins.appfeedback.preferences";
    private final String SHAREDPREF_KEY_SKIP_HELP = "com.outsystems.plugins.appfeedback.key.skiphelp";

    private final MobileECTController mobileECTController;
    private final Context context;

    private boolean appFeedbackAvailable;


    public OSAppFeedbackListener(AppCompatActivity activity, ViewGroup mainViewGroup, ViewGroup ectViewGroup, WebView currentWebView, String hostname) {

        this.context = activity;

        SharedPreferences sharedPref = context.getSharedPreferences(SHAREDPREF_NAME, Context.MODE_PRIVATE);
        boolean skipHelp = sharedPref.getBoolean(SHAREDPREF_KEY_SKIP_HELP, false);

        this.mobileECTController = new MobileECTController(activity,
                mainViewGroup,
                ectViewGroup,
                currentWebView,
                hostname,
                skipHelp,
                this);
    }

    @Override
    public void onSendFeedbackClickListener() {
        mobileECTController.sendFeedback();
    }

    @Override
    public void onCloseECTClickListener() {
        mobileECTController.closeECTNativeUI();
    }

    @Override
    public void onCloseECTHelperClickListener() {
        mobileECTController.setSkipECTHelper(true);
    }


    public boolean isAppFeedbackAvailable() {
        return appFeedbackAvailable;
    }


    public void openECT() {
        mobileECTController.openECTNativeUI();
        SharedPreferences sharedPref = this.context.getSharedPreferences(SHAREDPREF_NAME, Activity.MODE_PRIVATE);
        SharedPreferences.Editor editor = sharedPref.edit();
        editor.putBoolean(SHAREDPREF_KEY_SKIP_HELP, true);
        editor.commit();
    }

    /**
     * Public API for Plugin
     */

    public void handleDeviceReady() {
        this.mobileECTController.prepareECTData(result -> { /* Do Nothing */ });
    }

    private boolean isNetworkAvailable(Context context) {
        ConnectivityManager connectivityManager = (ConnectivityManager) context.getSystemService(Context.CONNECTIVITY_SERVICE);
        NetworkInfo activeNetworkInfo = connectivityManager.getActiveNetworkInfo();
        return activeNetworkInfo != null && activeNetworkInfo.isConnected();
    }

    public void handleECTAvailable(final OSECTProviderAPIHandler apiHandler) {
        if (isNetworkAvailable(context)) {
            mobileECTController.checkECTAvailability(result -> {
                appFeedbackAvailable = result;
                if (apiHandler != null)
                    apiHandler.execute(result);
            });
        } else {
            AlertDialog.Builder alert = new AlertDialog.Builder(context);
            alert.setTitle("Can't send feedback");
            alert.setMessage("Make sure your device has internet connection and try again.");
            alert.setPositiveButton("OK", (dialog, which) -> dialog.dismiss());
            alert.show();
        }
    }

    public void handleOpenECT(final OSECTProviderAPIHandler apiHandler) {
        boolean isAppFeedbackAvailable = this.isAppFeedbackAvailable();
        if (isAppFeedbackAvailable) {
            this.openECT();
        }
        if (apiHandler != null) {
            apiHandler.execute(isAppFeedbackAvailable);
        }
    }

}
