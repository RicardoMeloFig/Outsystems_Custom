package com.evothings;

import android.app.Notification;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.IBinder;
import android.os.Message;
import android.os.Messenger;
import android.os.RemoteException;

import android.support.v4.app.TaskStackBuilder;
import android.util.Log;

import com.estimote.sdk.Beacon;
import com.estimote.sdk.BeaconManager;
import com.estimote.sdk.Region;
import com.outsystems.android.R;

import java.lang.ref.WeakReference;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.concurrent.TimeUnit;

/**
 * Created by João Gonçalves (jppg) on 01/03/16.
 */
public class BeaconsMonitoringService extends Service {

    // Supported public Actions

    public static final String MSG_KEY_MONITORING_RESULT = "monitoringResult";

    /**
     * Action broadcasted when this service is ready and monitoring.
     * When receiving this action, any Activity can bind itself to this service.
     */
    public static final String SERVICE_READY_ACTION = "BeaconsMonitoringService.SERVICE_READY_ACTION";

    public static final int MSG_START_MONITORING_REGION = 1;
    public static final int MSG_STOP_MONITORING_REGION = 2;
    public static final int MSG_MONITOR_REGION_ON_ENTER = 3;
    public static final int MSG_MONITOR_REGION_ON_EXIT = 4;
    public static final int MSG_REGISTER_CLIENT = 5;
    public static final int MSG_UNREGISTER_CLIENT = 6;


    private static final String TAG = BeaconsMonitoringService.class.getSimpleName();

    /**
     * Messenger to handle incoming messages from clients
     */
    private Messenger mMessenger = null;

    /**
     * Remote messenger (client) to where the callbacks from BeaconManager will broadcast.
     */
    private Messenger mReplyTo;

    /**
     * Wheter this service is bound to any activity
     */
    private boolean mIsBound;

    private RegionsStore mRegionsStore = null;

    /**
     *
     */
    private boolean mIsBeaconManagerConnected = false;

    /**
     * BeaconManager to interact with EstimoteSDK
     */
    private BeaconManager mBeaconManager = null;
    private PrivateMonitoringListener mMonitoringListener = null;
    private PrivateServiceReadyCallback beaconManagerServiceReadyCb = null;
    private Set<String> monitoredRegionIds;
    public static final String NOTIFICATION_TAG = "OutSystemsBeaconNotifications";
    private NotificationManager mNotificationManager;

    @Override
    public void onCreate() {
        Log.d(TAG, "onCreate start");
        super.onCreate();
        mIsBound = false;

        if(mRegionsStore == null)
            mRegionsStore = new RegionsStore(this.getBaseContext());

        if (mMessenger == null) {
            mMessenger = new Messenger(new IncomingHandler(this));
        }

        if(mBeaconManager == null)
            mBeaconManager = new BeaconManager(this);

        mBeaconManager.setBackgroundScanPeriod(TimeUnit.SECONDS.toMillis(1), 5);

        if(mMonitoringListener == null) {
            mMonitoringListener = new PrivateMonitoringListener();
            mBeaconManager.setMonitoringListener(mMonitoringListener);
        }

        if(beaconManagerServiceReadyCb == null) {
            beaconManagerServiceReadyCb = new PrivateServiceReadyCallback(this);
            mBeaconManager.connect(beaconManagerServiceReadyCb);
        }

        if(monitoredRegionIds == null) {
            monitoredRegionIds = new HashSet<String>();
        }

        mNotificationManager = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);

        Log.d(TAG, "onCreate end");
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        Log.d(TAG, "onStartCommand");
        return START_STICKY;
    }

    @Override
    public void onDestroy() {
        Log.d(TAG, "onDestroy start");
        if (mBeaconManager != null) {
            mBeaconManager.disconnect();
        }
        Log.d(TAG, "onDestroy end");
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) {
        Log.d(TAG, "onBind start");
        mIsBound = true;
        Log.d(TAG, "onBind end");
        return mMessenger.getBinder();
    }

    @Override
    public boolean onUnbind(Intent intent) {
        Log.d(TAG, "onUnbind start");
        this.mReplyTo = null;
        this.mIsBound = false;
        Log.d(TAG, "onUnbind end");
        return true;
    }

    @Override
    public void onRebind(Intent intent) {
        Log.d(TAG, "onRebind start");
        this.mIsBound = true;
        Log.d(TAG, "onRebind end");
    }


    /**
     * Broadcast SERVICE_READY_ACTION to inform that this service is now monitoring for beacons.
     */
    private void broadcastReadyAction() {
        Log.d(TAG, "broadcastReadyAction start");
        final Intent intent = new Intent();
        intent.setAction(BeaconsMonitoringService.SERVICE_READY_ACTION);
        getBaseContext().sendBroadcast(intent);
        Log.d(TAG, "broadcastReadyAction end");
    }

    /**
     * @param region
     */
    public void startMonitoring(Region region) {
        Log.d(TAG, "startMonitoring start");
        if (region == null) {
            throw new NullPointerException("Region cannot be null");
        }

        if (mBeaconManager == null) {
            Log.i(TAG, "Can't start monitoring. BeaconManager wasn't initialised.");
        }

        if (!mIsBeaconManagerConnected) {
            Log.i(TAG, "Can't start monitoring. BeaconManager not connect. Are you sure you called connect?");
        }

        if (mBeaconManager != null && this.mIsBeaconManagerConnected) {
            Log.d(TAG, "startMonitoring: " + ((region != null) ? region.toString() : "null"));
            if(!monitoredRegionIds.contains(region.getIdentifier())) {
                mBeaconManager.startMonitoring(region);
                monitoredRegionIds.add(region.getIdentifier());
            }
            mRegionsStore.setRegion(region);
        }
        Log.d(TAG, "startMonitoring end");
    }

    /**
     * @param region
     */
    public void stopMonitoring(Region region) {
        Log.d(TAG, "stopMonitoring start");
        if (region == null) {
            throw new NullPointerException("Region cannot be null");
        }

        if (mBeaconManager == null) {
            Log.i(TAG, "Can't start monitoring. BeaconManager wasn't initialised.");
        }

        if (!mIsBeaconManagerConnected) {
            Log.i(TAG, "Can't start monitoring. BeaconManager not connect. Are you sure you called connect?");
        }

        if(monitoredRegionIds.contains(region.getIdentifier())){
            mBeaconManager.stopMonitoring(region);
            monitoredRegionIds.remove(region.getIdentifier());
        }
        mRegionsStore.remove(region.getIdentifier());
        Log.d(TAG, "stopMonitoring end");

    }

    private void registerLocallySavedRegions(){
        Log.d(TAG, "registerLocallySavedRegions start");
        if(mRegionsStore != null) {
            List<Region> allRegions = mRegionsStore.getAll();
            for (Region region : allRegions) {
                startMonitoring(region);
            }
        }
        Log.d(TAG, "registerLocallySavedRegions end");
    }

    /**
     * Callback for "Ready" event from BeaconManager.
     * Once onServiceReady is called, BeaconManager methods to start monitoring for
     * regions and/or beacons can be called.
     */
    private class PrivateServiceReadyCallback implements BeaconManager.ServiceReadyCallback {

        private WeakReference<BeaconsMonitoringService> mServiceRef = null;

        public PrivateServiceReadyCallback(BeaconsMonitoringService service) {
            mServiceRef = new WeakReference<BeaconsMonitoringService>(service);
        }

        @Override
        public void onServiceReady() {
            Log.d(TAG, "onServiceReady start");
            final BeaconsMonitoringService service = mServiceRef.get();
            if (service != null) {
                service.mIsBeaconManagerConnected = true;
                service.registerLocallySavedRegions();
                service.broadcastReadyAction();
            }
            Log.d(TAG, "onServiceReady end");
        }
    }

    /**
     * Local MonitoringListener for BeaconManager.
     */
    private class PrivateMonitoringListener implements BeaconManager.MonitoringListener {

        @Override
        public void onEnteredRegion(Region region, List<Beacon> list) {
            Log.d(TAG, System.identityHashCode(this) + " - onEnteredRegion start");
            region = mRegionsStore.getRegion(region.getIdentifier());

            if(region instanceof NotificationRegion) {
                if(isInIdleTime((NotificationRegion) region))
                    return;
            }

            // If service is bound and we have a Messenger to ReplyTo then send message to it
            if (BeaconsMonitoringService.this.mIsBound && BeaconsMonitoringService.this.mReplyTo != null) {

                Message monitoringResponseMsg = Message.obtain(null, MSG_MONITOR_REGION_ON_ENTER);
                monitoringResponseMsg.getData().putParcelable(MSG_KEY_MONITORING_RESULT, region);
                try {
                    BeaconsMonitoringService.this.mReplyTo.send(monitoringResponseMsg);
                } catch (RemoteException e) {
                    if(region instanceof NotificationRegion) {
                        postNotification((NotificationRegion) region, true);
                        Log.d(TAG, "Sent ENTERED notification because replyTo is gone....");
                    }
                }
                Log.d(TAG, "Sent ENTERED message to replyTo.");
            } else {
                if(region instanceof NotificationRegion) {
                    postNotification((NotificationRegion) region, true);
                    Log.d(TAG, "Sent ENTERED notification.");
                }
            }

            if(region instanceof NotificationRegion) {
                // Update the time of the notification
                NotificationRegion notificationRegion = (NotificationRegion) region;
                notificationRegion.setLastNotificationTime(System.currentTimeMillis());
                mRegionsStore.setRegion(notificationRegion);
            }

            Log.d(TAG, System.identityHashCode(this) + " - onEnteredRegion end");
        }

        @Override
        public void onExitedRegion(Region region) {
            Log.d(TAG, System.identityHashCode(this) + " - onExitedRegion start");
            region = mRegionsStore.getRegion(region.getIdentifier());

            if(region instanceof NotificationRegion) {
                if(isInIdleTime((NotificationRegion) region))
                    return;
            }

            // If service is bound and we have a Messenger to ReplyTo then send message to it
            if (BeaconsMonitoringService.this.mIsBound && BeaconsMonitoringService.this.mReplyTo != null) {

                Message monitoringResponseMsg = Message.obtain(null, MSG_MONITOR_REGION_ON_EXIT);
                monitoringResponseMsg.getData().putParcelable(MSG_KEY_MONITORING_RESULT, region);
                try {
                    BeaconsMonitoringService.this.mReplyTo.send(monitoringResponseMsg);
                } catch (RemoteException e) {
                    if(region instanceof NotificationRegion) {
                        postNotification((NotificationRegion) region, false);
                        Log.d(TAG, "Sent EXITED notification because replyTo is gone....");
                    }
                }
                Log.d(TAG, "Sent EXITED message to replyTo.");
            } else {
                if(region instanceof NotificationRegion) {
                    postNotification((NotificationRegion) region, false);
                    Log.d(TAG, "Sent EXITED notification.");
                }
            }

            if(region instanceof NotificationRegion) {
                // Update the time of the notification
                NotificationRegion notificationRegion = (NotificationRegion) region;
                notificationRegion.setLastNotificationTime(System.currentTimeMillis());
                mRegionsStore.setRegion(notificationRegion);
            }

            Log.d(TAG, System.identityHashCode(this) + " - onExitedRegion end");
        }
    }


    /**
     * Inner class to handle incoming messages from the clients
     */
    private class IncomingHandler extends Handler {

        private final WeakReference<BeaconsMonitoringService> mReference;

        public IncomingHandler(BeaconsMonitoringService service) {
            Log.d(TAG, "IncomingHandler::ctor");
            this.mReference = new WeakReference<BeaconsMonitoringService>(service);
        }

        @Override
        public void handleMessage(Message msg) {
            Log.d(TAG, "handleMessage start");
            final Bundle bundle = msg.getData();
            BeaconsMonitoringService service = mReference.get();

            switch (msg.what) {
                case MSG_START_MONITORING_REGION:
                    if (service != null) {
                        bundle.setClassLoader(Region.class.getClassLoader());
                        Region region = bundle.getParcelable("region");
                        Log.d(TAG, "MSG_START_MONITORING_REGION");
                        service.startMonitoring(region);
                    }
                    break;
                case MSG_STOP_MONITORING_REGION:
                    if (service != null) {
                        bundle.setClassLoader(Region.class.getClassLoader());
                        Region region = bundle.getParcelable("region");
                        Log.d(TAG, "MSG_STOP_MONITORING_REGION - " + ((region != null) ? region.toString() : "null"));
                        service.stopMonitoring(region);
                    }
                    break;
                case MSG_REGISTER_CLIENT:
                    if(service != null) {
                        service.mReplyTo = msg.replyTo;
                        Log.d(TAG, "MSG_REGISTER_CLIENT");
                    }
                    break;
                case MSG_UNREGISTER_CLIENT:
                    if (service != null) {
                        service.mReplyTo = null;
                        Log.d(TAG, "MSG_UNREGISTER_CLIENT");
                    }
                    break;
                default:
                    super.handleMessage(msg);
            }

            Log.d(TAG, "handleMessage end");

        }
    }

    private boolean isInIdleTime(NotificationRegion notificationRegion) {
        if(notificationRegion.getIdle() > 0 && notificationRegion.getLastNotificationTime() > 0) {
            // Elapsed time is in milliseconds
            long elapsedMs = System.currentTimeMillis() - notificationRegion.getLastNotificationTime();
            // Idle time is in minutes
            long elapsedMinutes = TimeUnit.MILLISECONDS.toMinutes(elapsedMs);
            // Do not show local notification if the elapsed time isn't higher than the idle time from
            // the notification.
            if(elapsedMinutes < notificationRegion.getIdle()) {
                return true;
            }
        }

        return false;
    }

    private void postNotification(NotificationRegion notificationRegion, Boolean entering) {
        Intent notifyIntent = null;

        // Only notify if we have all the necessary information to show
        if(entering && !(notificationRegion.getEnterTitle() != null && !notificationRegion.getEnterTitle().isEmpty()
                && notificationRegion.getEnterMessage() != null && !notificationRegion.getEnterMessage().isEmpty())) {
            return;
        }

        if(!entering && !(notificationRegion.getExitTitle() != null && !notificationRegion.getExitTitle().isEmpty()
                && notificationRegion.getExitMessage() != null && !notificationRegion.getExitMessage().isEmpty())) {
            return;
        }

        Context context = getApplicationContext();
        String packageName = context.getPackageName();

        notifyIntent = context.getPackageManager()
                .getLaunchIntentForPackage(packageName);


        if(notificationRegion.getDeeplink() != null && !notificationRegion.getDeeplink().isEmpty()) {
            notifyIntent.setData(Uri.parse(notificationRegion.getDeeplink()));
        }

        notificationRegion.setOpenedFromNotification(true);
        notifyIntent.putExtra("beacons.notification.data", JSONUtils.toJson(notificationRegion));
        notifyIntent.putExtra("beacons.notification.inside", entering);

        if (notifyIntent == null) {
            return;
        }

        Log.d(TAG, notifyIntent.toString());


        TaskStackBuilder stackBuilder = TaskStackBuilder.create(context);
        stackBuilder.addNextIntent(notifyIntent);
        PendingIntent pendingIntent = stackBuilder.getPendingIntent(
                0, PendingIntent.FLAG_UPDATE_CURRENT);

        // Set message depending entering or exit...
        Notification notification = new Notification.Builder(BeaconsMonitoringService.this)
                .setSmallIcon(R.drawable.icon)
                .setContentTitle(entering ? notificationRegion.getEnterTitle() : notificationRegion.getExitTitle())
                .setContentText(entering ? notificationRegion.getEnterMessage() : notificationRegion.getExitMessage())
                .setAutoCancel(true)
                .setContentIntent(pendingIntent)
                .build();

        notification.defaults |= Notification.DEFAULT_SOUND;
        notification.defaults |= Notification.DEFAULT_LIGHTS;

        mNotificationManager.notify(NOTIFICATION_TAG, notificationRegion.hashCode(), notification);
    }

    private Intent createLaunchIntent(NotificationRegion notificationRegion) {
        Intent notifyIntent = null;
        Context context = getApplicationContext();
        PackageManager pm = context.getPackageManager();
        String apppack = getPackageName();
        String name = "";
        try {
            if (pm != null) {
                ApplicationInfo app = context.getPackageManager().getApplicationInfo(apppack, 0);
                name = (String) pm.getApplicationLabel(app);
                notifyIntent = pm.getLaunchIntentForPackage(apppack);
                if(notificationRegion.getDeeplink() != null && !notificationRegion.getDeeplink().isEmpty()) {
                    notifyIntent.setData(Uri.parse(notificationRegion.getDeeplink()));
                }
                notifyIntent.setFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP);
            }
        } catch (PackageManager.NameNotFoundException e) {

        }

        return notifyIntent;
    }
}