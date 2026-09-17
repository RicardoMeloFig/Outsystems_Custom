package com.adobe.phonegap.push;

import android.annotation.SuppressLint;
import android.content.Context;
import android.os.Bundle;
import android.util.Log;

import com.google.firebase.messaging.FirebaseMessagingService;
import com.google.firebase.messaging.RemoteMessage;

import java.util.Map;

@SuppressLint("NewApi")
public class FCMService extends FirebaseMessagingService implements PushConstants {

    private static final String LOG_TAG = "Push_FCMService";

    @Override
    public void onMessageReceived(RemoteMessage message) {

        String from = message.getFrom();
        Log.d(LOG_TAG, "onMessage - from: " + from);

        Bundle extras = new Bundle();

        if (message.getNotification() != null) {
            extras.putString(TITLE, message.getNotification().getTitle());
            extras.putString(MESSAGE, message.getNotification().getBody());
        }
        for (Map.Entry<String, String> entry : message.getData().entrySet()) {
            extras.putString(entry.getKey(), entry.getValue());
        }

        Context applicationContext = getApplicationContext();
        if (NotificationHandlerUtil.isAvailableSender(applicationContext, from)) {
            NotificationHandlerUtil.handleMessage(applicationContext, extras, false);
        }
    }

}
