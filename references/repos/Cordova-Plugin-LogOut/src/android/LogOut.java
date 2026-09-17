package com.os.mobile.logout;

import android.app.AlertDialog;
import android.content.DialogInterface;
import android.webkit.CookieManager;

import com.outsystems.android.core.WebServicesClient;
import com.squareup.okhttp.internal.NamedRunnable;

import org.apache.cordova.CallbackContext;
import org.apache.cordova.CordovaPlugin;
import org.json.JSONArray;

/**
 * Created by vmfo on 03/05/15.
 * @author Vítor Oliveira (vitor.oliveira@outsystems.com)
 * @author João Pereira Gonçalves (joao.goncalves@outsystems.com)
 */
public class LogOut extends CordovaPlugin {

    private static final String LOG_OUT = "logOut";

    private static final String LOG_OUT_WEBVIEW = "logOutWebView";

    @Override
    public boolean execute(String action, JSONArray args, CallbackContext callbackContext) {

        if (action.equals(LOG_OUT)) {
            logOut();
        } else if (action.equals(LOG_OUT_WEBVIEW)) {
            logoutWebView();
        }
        return true;
    }

    private void logOut() {
        cordova.getActivity().finish();
    }

    private void logoutWebView() {
        AlertDialog.Builder builder = new AlertDialog.Builder(cordova.getActivity());
        builder.setTitle("Sign Off Verification");
        builder.setMessage("You're about to sign off. Are you sure?");

        builder.setPositiveButton("Yes", new DialogInterface.OnClickListener() {
            @Override
            public void onClick(DialogInterface dialogInterface, int i) {

                LogOut.this.cordova.getActivity().runOnUiThread(new NamedRunnable("LogoutRunnable") {
                    @Override
                    protected void execute() {
                        WebServicesClient.getInstance().getLoginCookies().clear();
                        CookieManager.getInstance().removeAllCookie();
                        LogOut.this.webView.loadUrl(webView.getUrl());
                    }
                });
            }
        });

        builder.setNegativeButton("Cancel", new DialogInterface.OnClickListener() {
            @Override
            public void onClick(DialogInterface dialogInterface, int i) {
                dialogInterface.dismiss();
            }
        });

        builder.show();
    }
}
