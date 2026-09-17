package com.outsystems.plugins.capacitorsslpinning.pinning;

import okhttp3.OkHttpClient;

public class OkHttpClientWrapper {

    private static OkHttpClientWrapper instance;

    private OkHttpClient client;

    private OkHttpClientWrapper() {
        if (client == null) {
            OkHttpClient.Builder clientBuilder = new OkHttpClient.Builder();
            clientBuilder.retryOnConnectionFailure(false);
            client = clientBuilder.build();
        }
    }

    public static OkHttpClientWrapper getInstance() {
        if (instance == null) {
            instance = new OkHttpClientWrapper();
        }

        return instance;
    }

    public OkHttpClient getOkHttpClient() {
        return client;
    }
}
