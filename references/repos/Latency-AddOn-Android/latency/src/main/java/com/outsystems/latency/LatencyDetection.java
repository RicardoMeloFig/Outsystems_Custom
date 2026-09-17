package com.outsystems.latency;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.DialogInterface;
import android.os.AsyncTask;
import android.util.Log;
import android.view.WindowManager;

import org.apache.http.client.ClientProtocolException;

import java.io.IOException;
import java.net.InetAddress;
import java.util.Timer;
import java.util.TimerTask;

/**
 * Created by vmfo on 30/03/15.
 */
public class LatencyDetection {

    private LatencyResult latencyResult;
    private Timer latencyTimer;
    private Activity activity;

    public LatencyDetection (final Activity activity, final String host, final int timeSchedule, final int latencyLimit, final String title, final String message, LatencyResult latencyResult){
        this.latencyResult = latencyResult;

        // Start timer to check latency
        latencyTimer = new Timer();
        latencyTimer.schedule(new TimerTask() {
            @Override
            public void run() {
                executeAsyncTask(host);
            }
        }, 0, timeSchedule);
    }

    public LatencyDetection(final Activity activity, final String host, final int timeSchedule, final int latencyLimit, final String title, final String message) {
        this.latencyResult = new LatencyResult() {
            @Override
            public void onSuccessResult(String result) {
                Log.e("OS - Latency", "Latency is: " + result);

                try {
                    double latencyResult = Double.parseDouble(result);

                    if (latencyResult > latencyLimit) {

                        AlertDialog.Builder builder = new AlertDialog.Builder(activity);

                        builder.setMessage(message)
                                .setTitle(title);
                        builder.setNegativeButton(activity.getString(android.R.string.ok), new DialogInterface.OnClickListener() {
                            public void onClick(DialogInterface dialog, int id) {
                                dialog.dismiss();
                            }
                        });
                        AlertDialog dialog = builder.create();
                        try {
                            dialog.show();
                        } catch (WindowManager.BadTokenException exp) {
                            Log.e("OS - Latency", exp.toString());
                        }
                    }
                } catch (NumberFormatException exp) {
                    Log.e("OS - Latency", exp.toString());
                }
            }

            @Override
            public void onErrorResult(String error) {
                Log.e("OS - Latency", "Problem with ping. Error --> " + error);
            }
        };

        // Start timer to check latency
        latencyTimer = new Timer();
        latencyTimer.schedule(new TimerTask() {
            @Override
            public void run() {
                executeAsyncTask(host);
            }

        }, 0, timeSchedule);

    }

    private void executeAsyncTask(String host) {
        new PingServer().execute(host);
    }

    class PingServer extends AsyncTask<String, Integer, String> {

        @Override
        protected String doInBackground(String... params) {
            try {
                String host = params[0];
                int timeOut = 3000;
                long BeforeTime = System.currentTimeMillis();
                boolean reachable = InetAddress.getByName(host).isReachable(timeOut);
                long AfterTime = System.currentTimeMillis();
                Long TimeDifference = AfterTime - BeforeTime;


                return String.valueOf(TimeDifference);
            } catch (ClientProtocolException e) {
                e.printStackTrace();
                return null;
            } catch (IOException e) {
                e.printStackTrace();
                return null;
            }
        }

        @Override
        protected void onPostExecute(String s) {
            super.onPostExecute(s);

            if (s == null) {
                latencyResult.onErrorResult("unknown");
            } else {
                latencyResult.onSuccessResult(s);
            }
        }
    }

    private long calculateAverage(Long[] times) {
        long sum = 0;
        if (times.length > 0) {
            for (Long time : times) {
                sum += time;
            }
            return sum / times.length;
        }
        return sum;
    }

    public void stopLatencyChecker() {
        if (latencyTimer != null) {
            latencyTimer.cancel();
        }
    }
}
