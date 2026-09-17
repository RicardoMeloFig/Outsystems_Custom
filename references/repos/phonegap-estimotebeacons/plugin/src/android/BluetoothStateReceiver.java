package com.evothings;

import android.bluetooth.BluetoothAdapter;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.util.Log;

/**
 * Created by jppg on 01/03/16.
 */
public class BluetoothStateReceiver extends BroadcastReceiver {

    private static final String TAG = "BluetoothStateReceiver";

    @Override
    public void onReceive(Context context, Intent intent) {
        String action = intent.getAction();
        if(BluetoothAdapter.ACTION_STATE_CHANGED.equals(action)) {
            int state = intent.getIntExtra(BluetoothAdapter.EXTRA_STATE, -1);
            switch (state) {
                case BluetoothAdapter.STATE_ON:
                    Log.d(TAG, "BluetoothAdapter.STATE_ON");
                    context.startService(new Intent(context, BeaconsMonitoringService.class));
                    break;
                case BluetoothAdapter.STATE_OFF:
                    context.stopService(new Intent(context, BeaconsMonitoringService.class));
                    Log.d(TAG, "BluetoothAdapter.STATE_OFF");
                    break;
                default:
                    break;
            }
        }
    }
}
