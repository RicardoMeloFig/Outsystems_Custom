package com.outsystems.outbuilding;

import android.content.Intent;
import android.os.Handler;
import android.support.v7.app.AppCompatActivity;
import android.os.Bundle;

public class SplashScreenActivity extends AppCompatActivity {

    private static final int SplashScreenDelay = 2000;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_splash_screen);

        delaySplashScreen();
    }

    private void delaySplashScreen() {
        new Handler().postDelayed(new Runnable() {
            @Override
            public void run() {
                goNextActivity();
            }
        }, SplashScreenDelay);
    }

    protected void goNextActivity() {
        Intent intent = new Intent(this, MainActivity.class);
        startActivity(intent);
        finish();
    }
}
