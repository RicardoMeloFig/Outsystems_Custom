package com.cowbell.cordova.geofence;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.location.LocationManager;

/**
 * Created by domingosgomes on 11/01/17.
 */
public class LocationProviderChangedReceiver extends BroadcastReceiver {
    private String GpsPreferences = "GpsPreferences";
    private String GpsSharePreferences = "GpsHasEnable";

    @Override
    public void onReceive(Context context, Intent intent) {
        boolean isGpsEnabled;
        if (intent.getAction().matches("android.location.PROVIDERS_CHANGED"))
        {
            LocationManager locationManager = (LocationManager) context.getSystemService(Context.LOCATION_SERVICE);
            isGpsEnabled = locationManager.isProviderEnabled(LocationManager.GPS_PROVIDER);
            Boolean GpsHasEnable = getGpsState(context);
            //Start your Activity if location was enabled:
            if (isGpsEnabled && !GpsHasEnable) {
                Logger.setLogger(new Logger(GeofencePlugin.TAG, context, false));
                GeoNotificationManager manager = new GeoNotificationManager(context);
                manager.loadFromStorageAndInitializeGeofences();
            }
            setGpsState(isGpsEnabled,context);
        }
    }

    public void setGpsState(Boolean state, Context context){
        SharedPreferences sharedPref = context.getSharedPreferences(GpsPreferences,Context.MODE_PRIVATE);
        SharedPreferences.Editor editor = sharedPref.edit();
        editor.putBoolean(GpsSharePreferences, state);
        editor.commit();
    }

    public Boolean getGpsState(Context context){
        SharedPreferences sharedPref =  context.getSharedPreferences(GpsPreferences,Context.MODE_PRIVATE);
        Boolean gpsState = sharedPref.getBoolean(GpsSharePreferences,true);
        return gpsState;
    }
}
