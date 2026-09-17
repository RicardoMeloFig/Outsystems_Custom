package com.outsystems.latency;

import android.app.Activity;
import android.content.Context;
import android.opengl.Visibility;
import android.os.Handler;
import android.util.AttributeSet;
import android.util.Log;
import android.view.View;
import android.view.animation.Animation;
import android.view.animation.Transformation;
import android.widget.ImageButton;
import android.widget.RelativeLayout;



import org.sufficientlysecure.htmltextview.HtmlTextView;

/**
 * Created by vitoroliveira on 20/04/15.
 */
public class LatencyWarning extends RelativeLayout {

    public final static String TAG = "LATENCY WARNING";
    private static int TIME_SCHEDULE = 180000; //Time defined in milliseconds
    private static int LATENCY_LIMIT = 600; //Time defined in milliseconds
    private static int TIME_TO_SHOW_WARNING = 10000;
    private static int COUNTER_RETRIES = 8;

    private int counterTry = 0;
    private LatencyDetection latencyDetection;
    private boolean endTimeToShow = false;

    // Views of Latency Add-on
    View view;
    RelativeLayout viewLatencyMessage;

    public LatencyWarning(Context context) {
        super(context);
        initView();
    }

    public LatencyWarning(Context context, AttributeSet attrs) {
        super(context, attrs);
        initView();
    }

    public LatencyWarning(Context context, AttributeSet attrs, int defStyle) {
        super(context, attrs, defStyle);
        initView();
    }

    private void initView() {
        view = inflate(getContext(), R.layout.view_latency_warning, null);
        addView(view);

        viewLatencyMessage = (RelativeLayout) view.findViewById(R.id.view_latency_message);
        view.findViewById(R.id.button_close_info).setOnClickListener(onClickListenerMessage);
        ImageButton buttonHelp = (ImageButton) view.findViewById(R.id.image_button_icon);
        buttonHelp.setOnClickListener(onClickListenerInfoLatency);
        (view.findViewById(R.id.view_latency_error)).setVisibility(View.GONE);
        //(view.findViewById(R.id.view_latency_message)).setVisibility(View.GONE);
    }

    public void startLatencyCheck(String host, Activity activity) {
        counterTry = (COUNTER_RETRIES - 1);
        latencyDetection = new LatencyDetection(activity, host, TIME_SCHEDULE, LATENCY_LIMIT, activity.getString(R.string.label_latency_dialog_message), activity.getString(R.string.label_latency_dialog_message), new LatencyResult() {
            @Override
            public void onSuccessResult(String result) {
                try {
                    double latencyResult = Double.parseDouble(result);
                    Log.i(TAG, "Latency --> " + latencyResult);
                    if (latencyResult > LATENCY_LIMIT) {
                        if (counterTry == COUNTER_RETRIES) {
                            addLatencyWarning();
                            endTimeToShow = false;
                            counterTry = 0;
                            final Handler handler = new Handler();
                            handler.postDelayed(new Runnable() {
                                @Override
                                public void run() {
                                    endTimeToShow = true;

                                    if(viewLatencyMessage.getVisibility() != View.VISIBLE){
                                        removeLatencyWarning(0);
                                    }
                                }
                            }, TIME_TO_SHOW_WARNING);

                        } else {
                            counterTry += 1;
                        }
                    }
                } catch (NumberFormatException exp) {
                    Log.e("OS - Latency", exp.toString());
                }
            }

            @Override
            public void onErrorResult(String error) {

            }
        });
    }

    public void stopLatencyCheck(){
        if(latencyDetection != null){
            latencyDetection.stopLatencyChecker();
        }
    }

    public OnClickListener onClickListenerInfoLatency = new OnClickListener() {

        @Override
        public void onClick(View v) {
            HtmlTextView textViewInfoWarning = (HtmlTextView) findViewById(R.id.text_view_info_warning);
            String html = "<body leftmargin='50'><p><font color='white'>Your connection is getting low, we detect a High-Latency in your network, probably<br>"
                    + "<ul style='list-style-type:circle'>"
                    + "<li>Your device is too far from the access point.</li>"
                    + "<li>Are in a place with low signal.</li>"
                    + "<li>There is interference from other devices.</li>"
                    + "</ul></font></p>"
                    + "</body>";
            //Spanned sp = Html.fromHtml( html );
            //textViewInfoWarning.setText(sp);
            textViewInfoWarning.setHtmlFromString(html, false);
            // textViewInfoWarning.setMovementMethod(LinkMovementMethod.getInstance());
            expand(viewLatencyMessage);
        }
    };

    private OnClickListener onClickListenerMessage = new OnClickListener() {
        @Override
        public void onClick(View view) {
            Log.i(TAG, "Collapse View Event");
            collapse(viewLatencyMessage, 0);

            if(endTimeToShow) {
                removeLatencyWarning(1000);
            }
        }
    };

    public void addLatencyWarning() {
        Log.i(TAG, "addLatencyWarning");
        final RelativeLayout viewLatencyError = (RelativeLayout) view.findViewById(R.id.view_latency_error);
        if (viewLatencyError.getVisibility() != View.VISIBLE) {
            expand(viewLatencyError);
        }
    }

    public void removeLatencyWarning(int delay) {
        Log.i(TAG, "removeLatencyWarning");
        final RelativeLayout viewLatencyError = (RelativeLayout) view.findViewById(R.id.view_latency_error);
        if (viewLatencyError.getVisibility() == View.VISIBLE) {
            collapse(viewLatencyError, delay);
        }
    }

    public static void expand(final View v) {
        v.measure(LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT);
        final int targetHeight = v.getMeasuredHeight();

        v.getLayoutParams().height = 0;
        v.setVisibility(View.VISIBLE);
        Animation a = new Animation() {
            @Override
            protected void applyTransformation(float interpolatedTime, Transformation t) {
                v.getLayoutParams().height = interpolatedTime == 1
                        ? LayoutParams.WRAP_CONTENT
                        : (int) (targetHeight * interpolatedTime);
                v.requestLayout();
            }

            @Override
            public boolean willChangeBounds() {
                return true;
            }
        };

        // 1dp/ms
        // a.setDuration((int)(targetHeight / v.getContext().getResources().getDisplayMetrics().density));
        a.setDuration(500);
        v.startAnimation(a);
    }

    public static void collapse(final View v, int delayTostart) {
        final int initialHeight = v.getMeasuredHeight();

        Animation a = new Animation() {
            @Override
            protected void applyTransformation(float interpolatedTime, Transformation t) {
                if (interpolatedTime == 1) {
                    v.setVisibility(View.INVISIBLE);
                    v.invalidate();
                } else {
                    v.getLayoutParams().height = initialHeight - (int) (initialHeight * interpolatedTime);
                    v.requestLayout();
                }
            }

            @Override
            public boolean willChangeBounds() {
                return true;
            }
        };

        // 1dp/ms
        // a.setDuration((int)(initialHeight / v.getContext().getResources().getDisplayMetrics().density));
        a.setDuration(500);
        a.setStartTime(delayTostart);
        v.startAnimation(a);
    }
}