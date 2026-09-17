package com.cowbell.cordova.geofence;

import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.util.Log;

import com.google.android.gms.common.ConnectionResult;
import com.google.android.gms.common.GoogleApiAvailability;
import com.google.android.gms.location.Geofence;

import org.apache.cordova.CallbackContext;

import java.util.ArrayList;
import java.util.List;

public class GeoNotificationManager {
    private Context context;
    private GeoNotificationStore geoNotificationStore;
    //private LocationClient locationClient;
    private Logger logger;
    private boolean connectionInProgress = false;
    private List<Geofence> geoFences;
    private PendingIntent pendingIntent;
    private GoogleServiceCommandExecutor googleServiceCommandExecutor;

    public GeoNotificationManager(Context context) {
        this.context = context;
        geoNotificationStore = new GeoNotificationStore(context);
        logger = Logger.getLogger();
        googleServiceCommandExecutor = new GoogleServiceCommandExecutor();
        pendingIntent = getTransitionPendingIntent();
        if (areGoogleServicesAvailable()) {
            logger.log(Log.DEBUG, "Google play services available");
        } else {
            logger.log(Log.WARN, "Google play services not available. Geofence plugin will not work correctly.");
        }
    }

    public void loadFromStorageAndInitializeGeofences() {
        List<GeoNotification> geoNotifications = geoNotificationStore.getAll();
        geoFences = new ArrayList<Geofence>();
        for (GeoNotification geo : geoNotifications) {
            if(!geo.notification.notificationShowed){
                geoFences.add(geo.toGeofence());
            }
        }
        if (!geoFences.isEmpty()) {
            googleServiceCommandExecutor.QueueToExecute(new AddGeofenceCommand(
                context, pendingIntent, geoFences));
        }
    }

    public List<GeoNotification> getWatched() {
        List<GeoNotification> geoNotifications = geoNotificationStore.getAll();
        return geoNotifications;
    }

    private boolean areGoogleServicesAvailable() {
        // Check that Google Play services is available
        int resultCode = GoogleApiAvailability.getInstance().isGooglePlayServicesAvailable(context);
//        int resultCode = GooglePlayServicesUtil.isGooglePlayServicesAvailable(context);

        // If Google Play services is available
        if (ConnectionResult.SUCCESS == resultCode) {
            return true;
        } else {
            return false;
        }
    }

    public void addGeoNotifications(List<GeoNotification> geoNotifications,
            final CallbackContext callback) {
        List<Geofence> newGeofences = new ArrayList<Geofence>();
        for (GeoNotification geo : geoNotifications) {
            newGeofences.add(geo.toGeofence());
        }
        AddGeofenceCommand geoFenceCmd = new AddGeofenceCommand(context,
                pendingIntent, newGeofences);

        geoFenceCmd.addListener(new AddGeofenceCommandListener(geoNotifications, geoNotificationStore, callback));

        googleServiceCommandExecutor.QueueToExecute(geoFenceCmd);
    }

    private class AddGeofenceCommandListener implements IGoogleServiceCommandListener {

        private List<GeoNotification> geoNotifications;
        private GeoNotificationStore geoNotificationStore;
        private CallbackContext callback;

        public AddGeofenceCommandListener(List<GeoNotification> geoNotifications, GeoNotificationStore geoNotificationStore, CallbackContext callback) {
            this.geoNotifications = geoNotifications;
            this.geoNotificationStore = geoNotificationStore;
            this.callback = callback;
        }

        @Override
        public void onCommandExecuted(boolean withSuccess) {

            if (withSuccess) {

                // Save into database
                for (GeoNotification geoNotification : geoNotifications) {
                    geoNotificationStore.setGeoNotification(geoNotification);
                }
                if (callback != null) {
                    callback.success();
                }

            } else {
                if (callback != null) {
                    callback.error("Failed to add geofence");
                }
            }
        }
    }

    public void removeGeoNotification(String id, final CallbackContext callback) {
        List<String> ids = new ArrayList<String>();
        ids.add(id);
        removeGeoNotifications(ids, callback);
    }

    public void removeGeoNotifications(final List<String> ids,
            final CallbackContext callback) {
        RemoveGeofenceCommand cmd = new RemoveGeofenceCommand(context, ids);
        if (callback != null) {
            cmd.addListener(new IGoogleServiceCommandListener() {
                @Override
                public void onCommandExecuted(boolean withSuccess) {
                    if(withSuccess) {
                        for (String id : ids) {
                            geoNotificationStore.remove(id);
                        }
                        callback.success();
                    } else {
                        callback.error("Failed to remove geofence.");
                    }
                }
            });
        }

        googleServiceCommandExecutor.QueueToExecute(cmd);
    }

    public void removeAllGeoNotifications(final CallbackContext callback) {
        List<GeoNotification> geoNotifications = geoNotificationStore.getAll();
        List<String> geoNotificationsIds = new ArrayList<String>();
        for (GeoNotification geo : geoNotifications) {
            geoNotificationsIds.add(geo.id);
        }
        removeGeoNotifications(geoNotificationsIds, callback);
    }

    /*
     * Create a PendingIntent that triggers an IntentService in your app when a
     * geofence transition occurs.
     */
    private PendingIntent getTransitionPendingIntent() {
        // Create an explicit Intent

        Intent intent = new Intent(context,
                ReceiveTransitionsIntentService.class);
        /*
         * Return the PendingIntent
         */
        logger.log(Log.DEBUG, "Geofence Intent created!");
        return PendingIntent.getService(context, 0, intent,
                PendingIntent.FLAG_UPDATE_CURRENT);
    }

}
