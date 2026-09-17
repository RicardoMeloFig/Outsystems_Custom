/*
Android implementation of Cordova plugin for Estimote Beacons.

JavaDoc for Estimote Android API: https://estimote.github.io/Android-SDK/JavaDocs/
*/

package com.evothings;

import android.content.BroadcastReceiver;
import android.content.ComponentName;
import android.content.Context;
import android.content.IntentFilter;
import android.content.ServiceConnection;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.IBinder;
import android.os.Message;
import android.os.Messenger;
import android.os.RemoteException;
import android.text.TextUtils;
import android.util.Log;

import android.bluetooth.BluetoothAdapter;
import android.content.Intent;

import com.estimote.sdk.*;
import com.estimote.sdk.cloud.model.*;
import com.estimote.sdk.connection.*;
import com.estimote.sdk.exception.*;

import org.apache.cordova.*;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.InputStream;
import java.io.StringWriter;
import java.io.PrintWriter;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.IllegalFormatException;
import java.util.Iterator;
import java.util.List;
import java.util.Set;
import java.util.UUID;

/**
 * Plugin class for the Estimote Beacon plugin.
 */
public class EstimoteBeacons extends CordovaPlugin
{
	private static final String LOGTAG = "EstimoteBeacons";
	private static final String ESTIMOTE_PROXIMITY_UUID = "B9407F30-F5F8-466E-AFF9-25556B57FE6D";
	private static final String ESTIMOTE_SAMPLE_REGION_ID = "EstimoteSampleRegion";
	private static final int REQUEST_ENABLE_BLUETOOTH = 1;

	private BeaconManager     mBeaconManager;
	private EstimoteSDK       mEstimoteSDK;
	private CordovaInterface  mCordovaInterface;

	private ArrayList<Beacon> mRangedBeacons;
	private BeaconConnected   mConnectedBeacon;
	private boolean           mIsConnected = false;


	// Maps and variables that keep track of Cordova callbacks.
	private HashMap<String, CallbackContext> mRangingCallbackContexts =
		new HashMap<String, CallbackContext>();

	private CallbackContext   mBluetoothStateCallbackContext;
	private CallbackContext   mBeaconConnectionCallback;
	private CallbackContext   mBeaconDisconnectionCallback;

	// todo: consider using pluginInitialize instead, per Cordova recommendation
	//   https://github.com/apache/cordova-android/blob/master/framework/src/org/apache/cordova/CordovaPlugin.java#L60-L61
	/**
	 * Plugin initialiser.
	 */
	@Override
	public void initialize(final CordovaInterface cordova, CordovaWebView webView)
	{
		Log.i(LOGTAG, "initialize");
		super.initialize(cordova, webView);
	}

	@Override
	protected void pluginInitialize() {
		Log.i(LOGTAG, "pluginInitialize");

		super.pluginInitialize();

		mCordovaInterface = cordova;
		mCordovaInterface.setActivityResultCallback(this);

		// Initialized receiver Messenger
		if (mMessenger == null) {
			mMessenger = new Messenger(new IncomingHandler());
		}

		if (mBeaconManager == null) {
			mBeaconManager = new BeaconManager(cordova.getActivity());
		}

		mBeaconManager.setErrorListener(new BeaconManager.ErrorListener() {
            @Override
            public void onError(Integer errorId) {
                Log.e(LOGTAG, "BeaconManager error: " + errorId);
            }
        });

		mRangedBeacons = new ArrayList<Beacon>();


		doBindService();
		registerServiceReadyBroadcastReceiver();
	}

	/**
	 * Plugin reset.
	 * Called when the WebView does a top-level navigation or refreshes.
	 */
	@Override
	public void onReset() {
		Log.i(LOGTAG, "onReset");

		disconnectBeaconManager();
		sendMonitoringServiceReady();
		mRangingCallbackContexts = new HashMap<String, CallbackContext>();
	}

	@Override
	public void onResume(boolean multitasking) {
		Log.i(LOGTAG, "onResume - " + multitasking);

        doBindService();
        registerServiceReadyBroadcastReceiver();

		super.onResume(multitasking);
	}

	@Override
	public void onPause(boolean multitasking) {
		Log.i(LOGTAG, "onPause - " + multitasking);

        unregisterServiceReadyBroadcastReceiver();
        doUnbindService();

		super.onPause(multitasking);
	}

	/**
	 * The final call you receive before your activity is destroyed.
	 */
	public void onDestroy() {
		Log.i(LOGTAG, "onDestroy");
		disconnectConnectedBeacon();
		disconnectBeaconManager();
		mMessenger = null;
	}

	/**
	 * Disconnect from the beacon manager.
	 */
	private void disconnectBeaconManager() {
		if (mBeaconManager != null && mIsConnected) {
			mBeaconManager.disconnect();
			mIsConnected = false;
		}
	}

	/**
	 * Entry point for JavaScript calls.
	 */
	@Override
	public boolean execute(
		String action,
		CordovaArgs args,
		final CallbackContext callbackContext)
		throws JSONException
	{
		if ("beacons_startRangingBeaconsInRegion".equals(action)) {
			startRangingBeaconsInRegion(args, callbackContext);
		}
		else if ("beacons_stopRangingBeaconsInRegion".equals(action)) {
			stopRangingBeaconsInRegion(args, callbackContext);
		}
		else if ("beacons_startMonitoringForRegion".equals(action)) {
			startMonitoringForRegion(args, callbackContext);
		}
		else if ("beacons_stopMonitoringForRegion".equals(action)) {
			stopMonitoringForRegion(args, callbackContext);
		}
		else if ("beacons_setupAppIDAndAppToken".equals(action)) {
			setupAppIDAndAppToken(args, callbackContext);
		}
		else if ("beacons_connectToBeacon".equals(action)) {
			connectToBeacon(args, callbackContext);
		}
		else if ("beacons_disconnectConnectedBeacon".equals(action)) {
			disconnectConnectedBeacon(args, callbackContext);
		}
		else if ("beacons_writeConnectedProximityUUID".equals(action)) {
			writeConnectedProximityUUID(args, callbackContext);
		}
		else if ("beacons_writeConnectedMajor".equals(action)) {
			writeConnectedMajor(args, callbackContext);
		}
		else if ("beacons_writeConnectedMinor".equals(action)) {
			writeConnectedMinor(args, callbackContext);
		}
		else if ("bluetooth_bluetoothState".equals(action)) {
			checkBluetoothState(args, callbackContext);
		}
		else if("initService".equals(action)) {
			initService(args, callbackContext);
		} else if("deviceReady".equals(action)) {
			deviceReady(args, callbackContext);
		} else {
			return false;
		}
		return true;
	}

	private CallbackContext mInitServiceCallbackContext = null;

	private boolean initService(CordovaArgs cordovaArgs, final CallbackContext callbackContext) {
		mInitServiceCallbackContext = callbackContext;
		sendMonitoringServiceReady();
		return true;
	}

	private void deviceReady(CordovaArgs cordovaArgs, final CallbackContext callbackContext){
        Intent intent = cordova.getActivity().getIntent();
        Intent launcherIntent = null;

		if (intent != null && intent.hasExtra("LAUNCHER_INTENT")) {
			launcherIntent = intent.getParcelableExtra("LAUNCHER_INTENT");
		} else {
            launcherIntent = intent;
        }

        if (launcherIntent != null) {

            String data = launcherIntent.getStringExtra("beacons.notification.data");
            boolean inside = launcherIntent.getBooleanExtra("beacons.notification.inside", false);
			if (data != null) {
				try{
					final JSONObject jsonObject = new JSONObject(data);
					jsonObject.put("state", inside ? "inside" : "outside");
					sendMonitoringUpdateToWebView(jsonObject);
                    launcherIntent.removeExtra("beacons.notification.data");
                    launcherIntent.removeExtra("beacons.notification.inside");
				} catch (JSONException e) {

				}
			}
		}
	}

	/**
	 * If Bluetooth is off, open a Bluetooth dialog.
	 */
	private void checkBluetoothState(
		CordovaArgs cordovaArgs,
		final CallbackContext callbackContext)
		throws JSONException
	{
		Log.i(LOGTAG, "checkBluetoothState");

		// Check that no Bluetooth state request is in progress.
		if (null != mBluetoothStateCallbackContext) {
			callbackContext.error("Bluetooth state request already in progress");
			return;
		}

		// Check if Bluetooth is enabled.
		BluetoothAdapter bluetoothAdapter = BluetoothAdapter.getDefaultAdapter();

		if (!bluetoothAdapter.isEnabled()) {
			// Open Bluetooth dialog on the UI thread.
			final CordovaPlugin self = this;
			mBluetoothStateCallbackContext = callbackContext;
			Runnable openBluetoothDialog = new Runnable() {
				public void run() {
					Intent enableIntent = new Intent(BluetoothAdapter.ACTION_REQUEST_ENABLE);
					mCordovaInterface.startActivityForResult(
						self,
						enableIntent,
						REQUEST_ENABLE_BLUETOOTH);
				}
			};
			mCordovaInterface.getActivity().runOnUiThread(openBluetoothDialog);
		}
		else {
			// Bluetooth is enabled, return the result to JavaScript,
			sendResultForBluetoothEnabled(callbackContext);
		}
	}

	/**
	 * Check if Bluetooth is enabled and return result to JavaScript.
	 */
	public void sendResultForBluetoothEnabled(CallbackContext callbackContext)
	{
		BluetoothAdapter bluetoothAdapter = BluetoothAdapter.getDefaultAdapter();
		if (bluetoothAdapter.isEnabled()) {
			callbackContext.success(1);
		}
		else {
			callbackContext.success(0);
		}
	}

	/**
	 * Called when the Bluetooth dialog is closed.
	 */
	@Override
	public void onActivityResult(int requestCode, int resultCode, Intent intent)
	{
		Log.i(LOGTAG, "onActivityResult");
		if (REQUEST_ENABLE_BLUETOOTH == requestCode) {
			sendResultForBluetoothEnabled(mBluetoothStateCallbackContext);
			mBluetoothStateCallbackContext = null;
		}
	}

	/**
	 * Start ranging for beacons.
	 */
	private void startRangingBeaconsInRegion(
		CordovaArgs cordovaArgs,
		final CallbackContext callbackContext)
		throws JSONException
	{
		Log.i(LOGTAG, "startRangingBeaconsInRegion");

		JSONObject json = cordovaArgs.getJSONObject(0);
		Region region = null;
		try {
			region = createRegion(json);
		} catch (NullPointerException e) {
			callbackContext.error("Invalid UUID");
			return;
		} catch (IllegalArgumentException e) {
			callbackContext.error("Invalid UUID");
			return;
		}
		// TODO: How to handle case when region already ranged?
		// Stop ranging then start again?
		// Currently, if ranging callback already exists we
		// do nothing, just return.
		String key = regionHashMapKey(region);
		if (null != mRangingCallbackContexts.get(key)) {
			return;
		}

		// Add callback to hash map.
		mRangingCallbackContexts.put(key, callbackContext);

		// Create ranging listener.
		mBeaconManager.setRangingListener(new PluginRangingListener());

		// If connected start ranging immediately, otherwise first connect.
		if (mIsConnected) {
			startRanging(region, callbackContext);
		} else {
			Log.i(LOGTAG, "connect");

			// todo: consider whether this holds up to several startRanging(...)
			//   calls before onServiceReady() fires
			final Region finalRegion = region;
			mBeaconManager.connect(new BeaconManager.ServiceReadyCallback() {
				@Override
				public void onServiceReady() {
					Log.i(LOGTAG, "onServiceReady");
					mIsConnected = true;
					startRanging(finalRegion, callbackContext);
				}
			});
		}

	}

	/**
	 * Helper method.
	 */
	private void startRanging(Region region, CallbackContext callbackContext)
	{
		Log.i(LOGTAG, "startRanging");
		mBeaconManager.startRanging(region);
	}

	/**
	 * Stop ranging for beacons.
	 */
	private void stopRangingBeaconsInRegion(
		CordovaArgs cordovaArgs,
		final CallbackContext callbackContext)
		throws JSONException
	{
		Log.i(LOGTAG, "stopRangingBeaconsInRegion");

		JSONObject json = cordovaArgs.getJSONObject(0);

		Region region = null;
		try{
			region = createRegion(json);
		} catch (NullPointerException e) {
			callbackContext.error("Invalid UUID");
			return;
		} catch (IllegalArgumentException e) {
			callbackContext.error("Invalid UUID");
			return;
		}

		// If ranging callback does not exist call error callback
		String key = regionHashMapKey(region);
		CallbackContext rangingCallback = mRangingCallbackContexts.get(key);
		if (null == rangingCallback) {
			callbackContext.error("Region not ranged");
			return;
		}

		// Remove ranging callback from hash map.
		mRangingCallbackContexts.remove(key);

		// Clear ranging callback on JavaScript side.
		PluginResult result = new PluginResult(PluginResult.Status.NO_RESULT);
		result.setKeepCallback(false);
		rangingCallback.sendPluginResult(result);

		// Stop ranging if connected.
		if (mIsConnected) {
			//try {
				Log.i(LOGTAG, "stopRanging");

				// Stop ranging.
				mBeaconManager.stopRanging(region);

				// Send back success.
				callbackContext.success();
		}
		else {
			callbackContext.error("Not connected");
		}
	}

	/**
	 * Start monitoring for region.
	 */
	private void startMonitoringForRegion(
			CordovaArgs cordovaArgs,
			final CallbackContext callbackContext)
			throws JSONException {
		Log.i(LOGTAG, "startMonitoringForRegion");

		JSONObject json = cordovaArgs.getJSONObject(0);

		Region region = JSONUtils.fromJson(json.toString());
        if(region == null) {
            callbackContext.error("Invalid region");
            return;
        }

		if (mServiceMessenger != null) {
			Message msg = Message.obtain(null, BeaconsMonitoringService.MSG_START_MONITORING_REGION);
			msg.getData().putParcelable("region", region);
			msg.replyTo = mMessenger;
			try {
				mServiceMessenger.send(msg);
                callbackContext.success();
			} catch (RemoteException e) {
                callbackContext.error("Unable to talk with background monitoring service.");
			}
		} else {
			callbackContext.error("Perhaps your bluetooth is off or background service isn't yet ready?");
		}
	}



	/**
	 * Stop monitoring for region.
	 */
	private void stopMonitoringForRegion(
			CordovaArgs cordovaArgs,
			final CallbackContext callbackContext)
			throws JSONException {
		Log.i(LOGTAG, "stopMonitoringForRegion");

		JSONObject json = cordovaArgs.getJSONObject(0);

        Region region = JSONUtils.fromJson(json.toString());
        if(region == null) {
            callbackContext.error("Invalid region");
            return;
        }

		// Stop monitoring if connected.
		if (mServiceMessenger != null) {
			Message msg = Message.obtain(null, BeaconsMonitoringService.MSG_STOP_MONITORING_REGION);
			msg.getData().putParcelable("region", region);
			msg.replyTo = mMessenger;
			try {
				mServiceMessenger.send(msg);
				callbackContext.success();
			} catch (RemoteException e) {
				callbackContext.error("Unable to talk with background monitoring service.");
			}
		} else {
            callbackContext.error("Perhaps your bluetooth is off or background service isn't yet ready?");
		}
	}

	/**
	 * Authenticate with Estimote Cloud
	 */
	private void setupAppIDAndAppToken(
		CordovaArgs cordovaArgs,
		final CallbackContext callbackContext)
		throws JSONException
	{
		Log.i(LOGTAG, "setupAppIDAndAppToken");

		if (mEstimoteSDK == null) {
			mEstimoteSDK = new EstimoteSDK();

			String appID = cordovaArgs.getString(0);
			String appToken = cordovaArgs.getString(1);
			EstimoteSDK.initialize(cordova.getActivity(), appID, appToken);
			PluginResult r = new PluginResult(PluginResult.Status.OK);
			callbackContext.sendPluginResult(r);
		} else {
			// todo consider including helpful info e.g. appID
			callbackContext.error("already authenticated to Estimote Cloud");
		}
	}

	/**
	 * Find beacon in rangedBeacons, with MAC address
	 */
	private Beacon findBeacon(String macAddress) {
		Log.i(LOGTAG, "findBeacon(String)");
		for (Iterator<Beacon> i = mRangedBeacons.iterator(); i.hasNext();) {
			Beacon beacon = i.next();
			if (beacon.getMacAddress().equals(macAddress)) {
				return beacon;
			}
		}

		return null;
	}

	/**
	 * Find beacon in rangedBeacons, with region params
	 */
	private Beacon findBeacon(String proximityUUID, int major, int minor) {
		Log.i(LOGTAG, "findBeacon(String, int, int)");
		for (Iterator<Beacon> i = mRangedBeacons.iterator(); i.hasNext();) {
			Beacon beacon = i.next();
			if (beacon.getProximityUUID().equals(proximityUUID) &&
					beacon.getMajor() == major &&
					beacon.getMinor() == minor) {
				return beacon;
			}
		}

		return null;
	}

	/**
	 * Find beacon in rangedBeacons, from JSON
	 */
	private Beacon findBeacon(JSONObject json) throws JSONException {
		String macAddress = json.optString("macAddress", "");

		if (!macAddress.equals("")) {
			return findBeacon(macAddress);
		} else {
			String proximityUUID = json.optString("proximityUUID", "");
			int major = json.optInt("major", -1);
			int minor = json.optInt("minor", -1);

			if (!proximityUUID.equals("") && major > -1 && minor > -1) {
				return findBeacon(proximityUUID, major, minor);
			}
		}

		return null;
	}

	// todo: consider mac address only version?
	/**
	 * Connect to beacon
	 */
	private void connectToBeacon(
		CordovaArgs cordovaArgs,
		final CallbackContext callbackContext)
		throws JSONException
	{
		Log.i(LOGTAG, "connectToBeacon");

		JSONObject json = cordovaArgs.getJSONObject(0);

		Beacon beacon = findBeacon(json);
		if (beacon == null) {
			callbackContext.error("could not find beacon");
			return;
		}

		// beacons are jealous creatures and don't like competition
		if (mConnectedBeacon != null &&
			!mConnectedBeacon.getMacAddress().equals(beacon.getMacAddress())) {
			disconnectConnectedBeacon();
		}

		mBeaconConnectionCallback = callbackContext;
		mConnectedBeacon = new BeaconConnected(
			cordova.getActivity(),
			beacon,
			new PluginConnectingListener()
		);

		mConnectedBeacon.authenticate();

		return;
	}

	/**
	 * Disconnect connected beacon
	 */
	private void disconnectConnectedBeacon() {
		Log.i(LOGTAG, "disconnectConnectedBeacon");

		if (mConnectedBeacon != null && mConnectedBeacon.isConnected()) {
			mConnectedBeacon.close();
			mConnectedBeacon = null;
		}
	}

	/**
	 * Disconnect connected beacon, c/o Cordova
	 */
	private void disconnectConnectedBeacon(
		CordovaArgs cordovaArgs,
		final CallbackContext callbackContext)
		throws JSONException
	{
		Log.i(LOGTAG, "disconnectConnectedBeacon (cordova)");

		mBeaconDisconnectionCallback = callbackContext;
		disconnectConnectedBeacon();
	}

	/**
	 * Write Proximity UUID to connected beacon
	 */
	private void writeConnectedProximityUUID(
		CordovaArgs cordovaArgs,
		final CallbackContext callbackContext)
		throws JSONException
	{
		Log.i(LOGTAG, "writeConnectedProximityUUID");

		if (mConnectedBeacon != null && mConnectedBeacon.isConnected()) {
            String uuid = cordovaArgs.getString(0);

            Log.i(LOGTAG, uuid);
            Log.i(LOGTAG, mConnectedBeacon.getBeacon().getProximityUUID().toString());
            Log.i(LOGTAG, String.valueOf(uuid.equals(mConnectedBeacon.getBeacon().getProximityUUID())));

            // already correct, skip
            if (uuid.equals(mConnectedBeacon.getBeacon().getProximityUUID())) {
                PluginResult r = new PluginResult(PluginResult.Status.OK);
                callbackContext.sendPluginResult(r);
            }

            try {
                UUID.fromString(uuid);
            } catch (Exception e) {
                callbackContext.error(e.getMessage());
            }

            BeaconConnection.WriteCallback writeCallback;
            writeCallback = new BeaconConnection.WriteCallback() {
                @Override
                public void onSuccess() {
                    PluginResult r = new PluginResult(PluginResult.Status.OK);
                    callbackContext.sendPluginResult(r);
                }

                @Override
                public void onError(EstimoteDeviceException e) {
                    callbackContext.error(e.getMessage());
                }
            };

			//TODO (jppg): Implement PropertyChanger
            //mConnectedBeacon.writeProximityUuid(uuid, writeCallback);
        }
	}

	/**
	 * Write Major to connected beacon
	 */
	private void writeConnectedMajor(
		CordovaArgs cordovaArgs,
		final CallbackContext callbackContext)
		throws JSONException
	{
		Log.i(LOGTAG, "writeConnectedMajor");

		if (mConnectedBeacon != null && mConnectedBeacon.isConnected()) {
            int major = cordovaArgs.getInt(0);

            // already correct, skip
            if (major == mConnectedBeacon.getBeacon().getMajor()) {
                PluginResult r = new PluginResult(PluginResult.Status.OK);
                callbackContext.sendPluginResult(r);
            }

           if (major == 0) {
                callbackContext.error("major cannot be 0");
                return;
            }

            BeaconConnection.WriteCallback writeCallback;
            writeCallback = new BeaconConnection.WriteCallback() {
                @Override
                public void onSuccess() {
                    PluginResult r = new PluginResult(PluginResult.Status.OK);
                    callbackContext.sendPluginResult(r);
                }

                @Override
                public void onError(EstimoteDeviceException e) {
                    callbackContext.error(e.getMessage());
                }
            };

			//TODO (jppg): Implement PropertyChanger
            //mConnectedBeacon.writeMajor(major, writeCallback);
        }
	}

	/**
	 * Write Minor to connected beacon
	 */
	private void writeConnectedMinor(
		CordovaArgs cordovaArgs,
		final CallbackContext callbackContext)
		throws JSONException
	{
		Log.i(LOGTAG, "writeConnectedMinor");

		if (mConnectedBeacon != null && mConnectedBeacon.isConnected()) {
            int minor = cordovaArgs.getInt(0);

            // already correct, skip
            if (minor == mConnectedBeacon.getBeacon().getMinor()) {
                PluginResult r = new PluginResult(PluginResult.Status.OK);
                callbackContext.sendPluginResult(r);
            }

            if (minor == 0) {
                callbackContext.error("minor cannot be 0");
                return;
            }

            BeaconConnection.WriteCallback writeCallback;
            writeCallback = new BeaconConnection.WriteCallback() {
                @Override
                public void onSuccess() {
                    PluginResult r = new PluginResult(PluginResult.Status.OK);
                    callbackContext.sendPluginResult(r);
                }

                @Override
                public void onError(EstimoteDeviceException e) {
                    callbackContext.error(e.getMessage());
                }
            };
			//TODO (jppg): Implement PropertyChanger
            //mConnectedBeacon.writeMinor(minor, writeCallback);
        }
	}

	/**
	 * Create JSON object representing beacon info.
	 *
	 * beaconInfo format:
	 * {
	 *	 region: region,
	 *	 beacons: array of beacon
	 * }
	 */
	private JSONObject makeJSONBeaconInfo(Region region, List<Beacon> beacons)
		throws JSONException
	{
		// Create JSON object.
		JSONObject json = new JSONObject();
		json.put("region", makeJSONRegion(region));
		json.put("beacons", makeJSONBeaconArray(beacons));
		return json;
	}

	/**
	 * Create JSON object representing a region.
	 */
	private static JSONObject makeJSONRegion(Region region)
		throws JSONException
	{
		return makeJSONRegion(region, null);
	}

	/**
	 * Create JSON object representing a region in the given state.
	 */
	private static JSONObject makeJSONRegion(Region region, String state)
		throws JSONException
	{
		JSONObject json = new JSONObject();
		json.put("identifier", region.getIdentifier());
		json.put("uuid", region.getProximityUUID());
		json.put("major", region.getMajor());
		json.put("minor", region.getMinor());
		if (state != null) {
			json.put("state", state);
		}
		return json;
	}

	/**
	 * Create JSON object representing a beacon list.
	 */
	private JSONArray makeJSONBeaconArray(List<Beacon> beacons)
		throws JSONException
	{
		JSONArray jsonArray = new JSONArray();
		for (Beacon b : beacons) {
			// Compute proximity value.
			Utils.Proximity proximityValue = Utils.computeProximity(b);
			int proximity = 0; // Unknown.
			if (Utils.Proximity.IMMEDIATE == proximityValue) { proximity = 1; }
			else if (Utils.Proximity.NEAR == proximityValue) { proximity = 2; }
			else if (Utils.Proximity.FAR == proximityValue) { proximity = 3; }

			// Compute distance value.
			double distance = Utils.computeAccuracy(b);

			// Normalize UUID.
			String uuid = Utils.normalizeProximityUUID(b.getProximityUUID().toString());

			// Construct JSON object for beacon.
			JSONObject json = new JSONObject();
			json.put("major", b.getMajor());
			json.put("minor", b.getMinor());
			json.put("rssi", b.getRssi());
			json.put("measuredPower", b.getMeasuredPower());
			json.put("proximityUUID", uuid);
			json.put("proximity", proximity);
			json.put("distance", distance);
			//json.put("name", b.getName());
			json.put("macAddress", b.getMacAddress());
			jsonArray.put(json);
		}
		return jsonArray;
	}

    private String regionHashMapKey(String uuid, Integer major, Integer minor) {
        if (uuid == null) {
            uuid = "0";
        }

        if (major == null) {
            major = 0;
        }

        if (minor == null) {
            minor = 0;
        }

		// use % for easier decomposition
        return uuid + "%" + major + "%" + minor;
    }

	private String regionHashMapKey(Region region)
	{
		UUID uuid = region.getProximityUUID();
		Integer major = region.getMajor();
		Integer minor = region.getMinor();
		if(uuid == null) {
			return regionHashMapKey(null, major, minor);
		} else {
			return regionHashMapKey(uuid.toString(), major, minor);
		}
	}

	/**
	 * Create a Region object from Cordova arguments.
	 */
	private Region createRegion(JSONObject json) throws NullPointerException, IllegalArgumentException {
        // null ranges all regions, if unset
        String uuidString = json.optString("uuid", null);
		UUID uuid = null;
		if(uuidString != null) {
			uuid = UUID.fromString(uuidString);
		}
        Integer major = optUInt16Null(json, "major");
        Integer minor = optUInt16Null(json, "minor");


        String identifier = json.optString(
            "identifier",
            regionHashMapKey(uuidString, major, minor)
        );

		return new Region(identifier, uuid, major, minor);
	}

	/**
	 * Returns the value mapped by name if it exists and is a positive integer
	 * no larger than 0xFFFF.
	 * Returns null otherwise.
	 */
	private Integer optUInt16Null(JSONObject json, String name) {
		int i = json.optInt(name, -1);
		if (i < 0 || i > (0xFFFF)) {
			return null;
		}
		else {
			return new Integer(i);
		}
	}

	/**
	 * Listener for ranging events.
	 */
	class PluginRangingListener implements BeaconManager.RangingListener {
		@Override
		public void onBeaconsDiscovered(Region region, List<Beacon> beacons) {
			// Note that results are not delivered on UI thread.

			Log.i(LOGTAG, "onBeaconsDiscovered");

			try {
				// store in plugin
				mRangedBeacons.clear();
				mRangedBeacons.addAll(beacons);

				// Find region callback.
				String key = regionHashMapKey(region);
				CallbackContext rangingCallback = mRangingCallbackContexts.get(key);
				if (null == rangingCallback) {
					// No callback found.
					Log.e(LOGTAG,
						"onBeaconsDiscovered no callback found for key: " + key);
					return;
				}

				// Create JSON beacon info object.
				JSONObject json = makeJSONBeaconInfo(region, beacons);

				// Send result to JavaScript.
				PluginResult r = new PluginResult(PluginResult.Status.OK, json);
				r.setKeepCallback(true);
				rangingCallback.sendPluginResult(r);
			}
			catch(JSONException e) {
				Log.e(LOGTAG, "onBeaconsDiscovered error:", e);
			}
		}
	}



	/**
	 * Listener for beacon connection events
	 */
	class PluginConnectingListener implements BeaconConnection.ConnectionCallback
    {
        @Override public void onAuthorized(BeaconInfo beaconInfo) {

        }

		@Override
		public void onConnected(BeaconInfo beaconInfo) {
			CallbackContext callback = mBeaconConnectionCallback;

			if (callback == null) {
				return;
			}

			try {
				JSONObject json = new JSONObject();

				// add beaconInfo
				json.put(
						"batteryLifeExpectancyInDays",
						beaconInfo.batteryLifeExpectancyInDays
				);
				json.put("color", beaconInfo.color.toString());
				json.put("macAddress", beaconInfo.macAddress);
				json.put("major", beaconInfo.major);
				json.put("minor", beaconInfo.minor);
				json.put("name", beaconInfo.name);
				json.put("uuid", beaconInfo.uuid);

				Log.i(LOGTAG, "2");
				// add beaconInfo.settings
				BeaconInfoSettings settings = beaconInfo.settings;
				JSONObject jsonSettings = new JSONObject();
				jsonSettings.put(
						"advertisingIntervalMillis",
						settings.advertisingIntervalMillis
				);
				jsonSettings.put("batteryLevel", settings.batteryLevel);
				jsonSettings.put(
						"broadcastingPower",
						settings.broadcastingPower
				);
				jsonSettings.put("firmware", settings.firmware);
				jsonSettings.put("hardware", settings.hardware);

				Log.i(LOGTAG, "3");
				// finish up response param
				json.put("settings", jsonSettings);

				Log.i(LOGTAG, "4");
				Log.i(LOGTAG, json.toString());
				// pass back to web
				PluginResult r = new PluginResult(PluginResult.Status.OK, json);
				callback.sendPluginResult(r);
			} catch (JSONException e) {
				Log.i(LOGTAG, "inError");
				String msg;
				msg = "connection succeeded, could not marshall object: ";
				msg = msg.concat(e.getMessage());

				callback.error(msg);
			}

			// cleanup
			mBeaconConnectionCallback = null;
		}

		@Override public void onAuthenticationError(EstimoteDeviceException e) {
            CallbackContext callback = mBeaconConnectionCallback;

            if (callback == null) {
                return;
            }

            // pass back to js
            callback.error(e.getMessage());

            // print stacktrace to android logs
            StringWriter sw = new StringWriter();
            PrintWriter pw = new PrintWriter(sw);
            e.printStackTrace(pw);
            Log.e(LOGTAG, sw.toString());

            // cleanup
            mBeaconConnectionCallback = null;
        }

        @Override public void onDisconnected() {
            CallbackContext callback = mBeaconDisconnectionCallback;

            if (callback == null) {
                return;
            }

            PluginResult r = new PluginResult(PluginResult.Status.OK);
            callback.sendPluginResult(r);

            // cleanup
            mBeaconDisconnectionCallback = null;
        }
    }

    public class BeaconConnected extends BeaconConnection {
        private Beacon mBeacon;

        public BeaconConnected(
            Context context,
            Beacon beacon,
            BeaconConnection.ConnectionCallback connectionCallback
        ) {
            super(context, beacon, connectionCallback);
            this.mBeacon = beacon;
        }

        public Beacon getBeacon() {
            return mBeacon;
        }
    }

	//---------------------------------------
	//
	//
	//
	//---------------------------------------


	/**
	 * Messenger to receive messages from service;
	 */
	private Messenger mMessenger = null;

	/**
	 * Messenger to send messages to service
	 */
	private Messenger mServiceMessenger = null;
	private boolean mBound = false;
	private boolean mBeaconsServiceConnected = false;

	/**
	 * Broadcast receiver to bind to the service in case the application was opened but the service was not yet running.
	 */
	private ServiceReadyBroadcastReceiver mServiceReadyBroadcastReceiver = null;

	private class ServiceReadyBroadcastReceiver extends  BroadcastReceiver {
		@Override
		public void onReceive(Context context, Intent intent) {
			Log.d(LOGTAG, "ServiceReadyBroadcastReceiver::onReceive start");
			doBindService();
			Log.d(LOGTAG, "ServiceReadyBroadcastReceiver::onReceive end");
		}
	}


	private void registerServiceReadyBroadcastReceiver() {
		Log.d(LOGTAG, "registerServiceReadyBroadcastReceiver start");
		IntentFilter intentFilter = new IntentFilter(BeaconsMonitoringService.SERVICE_READY_ACTION);
		if(mServiceReadyBroadcastReceiver == null) {
			mServiceReadyBroadcastReceiver = new ServiceReadyBroadcastReceiver();
		}
		final Context context = this.webView.getContext();
		context.registerReceiver(mServiceReadyBroadcastReceiver, intentFilter);
		Log.d(LOGTAG, "registerServiceReadyBroadcastReceiver end");
	}

	private void unregisterServiceReadyBroadcastReceiver() {
		Log.d(LOGTAG, "unregisterServiceReadyBroadcastReceiver start");
		final Context context = this.webView.getContext();
		context.unregisterReceiver(mServiceReadyBroadcastReceiver);
		Log.d(LOGTAG, "unregisterServiceReadyBroadcastReceiver end");
	}

	private void doBindService(){
		Log.d(LOGTAG, "doBindService start");
		final Context context = this.webView.getContext();
		if(!mBound) {
			context.startService(new Intent(context, BeaconsMonitoringService.class));
			mBound = context.bindService(new Intent(context, BeaconsMonitoringService.class),
					mServiceConnection, Context.BIND_AUTO_CREATE);
		}
		Log.d(LOGTAG, "doBindService end");
	}

	private void doUnbindService(){
		Log.d(LOGTAG, "doUnBindService start");
		if (mServiceMessenger != null) {
			try{
				Message msg = Message.obtain(null, BeaconsMonitoringService.MSG_UNREGISTER_CLIENT);
				mServiceMessenger.send(msg);
			} catch (RemoteException re) {
				Log.e(LOGTAG, re.getMessage(), re);
			}

		}
		final Context context = this.webView.getContext();
		context.unbindService(mServiceConnection);
		mBound = false;
		mBeaconsServiceConnected = true;
		Log.d(LOGTAG, "doUnBindService end");
	}

	private ServiceConnection mServiceConnection = new ServiceConnection() {

		@Override
		public void onServiceConnected(ComponentName name, IBinder service) {
			Log.d(LOGTAG, "onServiceConnected start");
			EstimoteBeacons.this.mServiceMessenger = new Messenger(service);
			// Send a message to the service to register our-self as a "replier"
			try{
				Message msg = Message.obtain(null, BeaconsMonitoringService.MSG_REGISTER_CLIENT);
				msg.replyTo = mMessenger;
				mServiceMessenger.send(msg);
			} catch (RemoteException re) {
				Log.e(LOGTAG, re.getMessage(), re);
			}

			EstimoteBeacons.this.mBeaconsServiceConnected = true;
			EstimoteBeacons.this.mBound = true;
			EstimoteBeacons.this.sendMonitoringServiceReady();

			Log.d(LOGTAG, "onServiceConnected end");
		}

		@Override
		public void onServiceDisconnected(ComponentName name) {
			Log.d(LOGTAG, "onServiceDisconnected start");
			EstimoteBeacons.this.mServiceMessenger = null;
			EstimoteBeacons.this.mBound = false;
			EstimoteBeacons.this.mBeaconsServiceConnected = false;
			Log.d(LOGTAG, "onServiceDisconnected end");
		}
	};

	private class IncomingHandler extends Handler {
		@Override
		public void handleMessage(Message msg) {
			Log.d(LOGTAG, "handleMessage start");

            final Bundle bundle = msg.getData();

			switch (msg.what) {
                case BeaconsMonitoringService.MSG_MONITOR_REGION_ON_ENTER: {
                    bundle.setClassLoader(Region.class.getClassLoader());
                    final Region region = bundle.getParcelable(BeaconsMonitoringService.MSG_KEY_MONITORING_RESULT);
                    sendMonitoringRegionEnterUpdate(region);
                    break;
                }
                case BeaconsMonitoringService.MSG_MONITOR_REGION_ON_EXIT:
                {
                    bundle.setClassLoader(Region.class.getClassLoader());
                    final Region region = bundle.getParcelable(BeaconsMonitoringService.MSG_KEY_MONITORING_RESULT);
                    sendMonitoringRegionExitUpdate(region);
                    break;
                }
				default:
					Log.d(LOGTAG, "Received a msg: " + msg.what);
					super.handleMessage(msg);
			}
			Log.d(LOGTAG, "handleMessage end");
		}
	}

	private void sendMonitoringServiceReady() {
		// Inform WebView that we are now ready to register regions
		try {
			if(mBeaconsServiceConnected && mInitServiceCallbackContext != null) {
				Log.d(LOGTAG, "Informing webview that we're ready!");
				JSONObject res = new JSONObject();
				res.put("ready", true);
				PluginResult pluginResult = new PluginResult(PluginResult.Status.OK, res);
				pluginResult.setKeepCallback(true);
				mInitServiceCallbackContext.sendPluginResult(pluginResult);
			}
		} catch (JSONException e){

		}
	}

	private void sendMonitoringRegionEnterUpdate(Region region) {
        try{
            final JSONObject jsonObject = JSONUtils.toJSONObject(region, "inside");
            if(jsonObject != null) {
                sendMonitoringUpdateToWebView(jsonObject);
            }
        } catch (JSONException e) {
            Log.e(LOGTAG, "sendMonitoringRegionEnterUpdate error: ", e);
        }
	}

    private void sendMonitoringRegionExitUpdate(Region region) {
        try{
            final JSONObject jsonObject = JSONUtils.toJSONObject(region, "outside");
            if(jsonObject != null) {
                sendMonitoringUpdateToWebView(jsonObject);
            }
        } catch (JSONException e) {
            Log.e(LOGTAG, "sendMonitoringRegionExitUpdate error: ", e);
        }
	}

    private void sendMonitoringUpdateToWebView(JSONObject jsonObject) {
        PluginResult pr = new PluginResult(PluginResult.Status.OK, jsonObject);
        pr.setKeepCallback(true);
        webView.sendPluginResult(pr, "EstimoteBeaconsStaticChannel");
        Log.d(LOGTAG, "sendMonitoringRegionEnterUpdate");
    }
}
