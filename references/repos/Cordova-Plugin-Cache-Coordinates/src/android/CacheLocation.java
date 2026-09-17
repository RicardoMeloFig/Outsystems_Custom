package com.os.mobile.cachelocation;

import android.location.Location;
import android.util.Log;

import org.apache.cordova.CallbackContext;
import org.apache.cordova.CordovaPlugin;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.sql.Timestamp;

/**
 * Created by vitoroliveira on 03/08/15.
 */
public class CacheLocation extends CordovaPlugin {

    public static final String ACTION_INIT_CACHE_LOCATION = "initCacheLocation";
    public static final String ACTION_GET_CACHE_LOCATION = "getCacheLocation";
    public static final String ACTION_STOP_CACHE_LOCATION = "stopCacheLocation";

    private LocationTracker locationTracker;

    @Override
    public boolean execute(String action, JSONArray args, CallbackContext callbackContext) throws JSONException {

        if (ACTION_INIT_CACHE_LOCATION.equals(action)) {
            initCacheLocation(callbackContext, args);
        } else if (ACTION_GET_CACHE_LOCATION.equals(action)) {
            getCacheLocation(callbackContext, args);
        } else if (ACTION_STOP_CACHE_LOCATION.equals(action)) {
            stopCacheLocation(callbackContext, args);
        }
        return true;
    }

    @Override
    public void onResume(boolean multitasking) {
        super.onResume(multitasking);
        if(locationTracker != null) {
            locationTracker.startListen();
        }
    }

    @Override
    public void onPause(boolean multitasking) {
        super.onPause(multitasking);

        if(locationTracker != null) {
            locationTracker.stopListen();
        }
    }

    private void initCacheLocation(CallbackContext callbackContext, JSONArray args) throws JSONException {
        long timeBetweenUpdates = 0;
        float metersBetweenUpdates = 0;
        if(args != null && args.length() > 0) {
            timeBetweenUpdates = Long.parseLong(args.get(0).toString());
            metersBetweenUpdates = args.getInt(1);
        }
        TrackerSettings settings =
                new TrackerSettings();
        settings.setUseGPS(true);
        settings.setUseNetwork(true);
        settings.setUsePassive(true);
        if(timeBetweenUpdates > 0)
            settings.setTimeBetweenUpdates(timeBetweenUpdates);
        if(metersBetweenUpdates > 0)
            settings.setMetersBetweenUpdates(metersBetweenUpdates);

        locationTracker = new LocationTracker(cordova.getActivity(), settings) {

            @Override
            public void onLocationFound(Location location) {
                // Do some stuff when a new location has been found.
                java.util.Date date = new java.util.Date();
                LocationStore.getInstance().setNewLocation(String.valueOf(location.getLatitude()), String.valueOf(location.getLongitude()), new Timestamp(date.getTime()).toString());
            }

            @Override
            public void onTimeout() {

            }
        };
        callbackContext.success();
    }

    private void getCacheLocation(CallbackContext callbackContext, JSONArray args) {

        if (LocationStore.getInstance().getLatitude() != null) {
            try {
                JSONObject r = new JSONObject();
                r.put("latitude", LocationStore.getInstance().getLatitude());
                r.put("longitude", LocationStore.getInstance().getLongitude());
                r.put("timestamp", LocationStore.getInstance().getTimeStamp());
                callbackContext.success(r);
            } catch (JSONException e) {
                Log.e("", e.toString());
            }
        } else {
            callbackContext.error("There isn't coordinates");
        }
    }

    private void stopCacheLocation (CallbackContext callbackContext, JSONArray args) {
        if(locationTracker != null) {
            locationTracker.stopListen();
            callbackContext.success();
        } else {
            callbackContext.error("Location Tracker isn't running");
        }
    }
}
