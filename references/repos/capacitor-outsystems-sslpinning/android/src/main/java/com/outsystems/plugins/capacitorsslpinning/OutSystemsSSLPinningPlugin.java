package com.outsystems.plugins.capacitorsslpinning;

import androidx.annotation.NonNull;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;
import com.outsystems.plugins.capacitorsslpinning.pinning.OkHttpClientWrapper;
import java.io.IOException;
import java.security.cert.CertificateException;
import java.util.concurrent.TimeUnit;
import javax.net.ssl.SSLHandshakeException;
import javax.net.ssl.SSLPeerUnverifiedException;
import okhttp3.Call;
import okhttp3.Callback;
import okhttp3.ConnectionPool;
import okhttp3.OkHttpClient;
import okhttp3.Request;
import okhttp3.Response;

@CapacitorPlugin(name = "OutSystemsSSLPinning")
@SuppressWarnings("unused")
public class OutSystemsSSLPinningPlugin extends Plugin {

    // See https://square.github.io/okhttp/3.x/okhttp/okhttp3/ConnectionPool.html
    private static final int CON_MAX_IDLE_CONNECTIONS_DEFAULT = 5;
    private static final int CON_KEEP_ALIVE_DEFAULT = 300;
    private static final String ERROR_CODE_CERTIFICATE = "1";
    private static final String ERROR_CODE_GENERIC = "2";

    @PluginMethod
    @SuppressWarnings("unused")
    public void checkCertificate(PluginCall call) {
        String url = call.getString("url");
        request(url, call);
    }

    private void request(final String url, final PluginCall pluginCall) {
        try {
            Request request = new Request.Builder().url(url).build();

            int timeout = 10000;

            OkHttpClient.Builder builder = getHttpClientBuilder()
                .connectTimeout(timeout, TimeUnit.MILLISECONDS)
                .readTimeout(timeout, TimeUnit.MILLISECONDS);
            OkHttpClient client = builder.build();

            Call call = client.newCall(request);

            call.enqueue(
                new Callback() {
                    @Override
                    public void onFailure(@NonNull Call call, @NonNull IOException e) {
                        if (
                            e instanceof SSLPeerUnverifiedException ||
                            (e instanceof SSLHandshakeException && e.getCause() != null && e.getCause() instanceof CertificateException)
                        ) {
                            pluginCall.reject(
                                "SSLPinning found a issue with the configured certificate for the url!",
                                ERROR_CODE_CERTIFICATE
                            );
                        } else {
                            pluginCall.reject("SSLPinning found some problem with the request!", ERROR_CODE_GENERIC);
                        }
                    }

                    @Override
                    public void onResponse(@NonNull Call call, @NonNull Response response) {
                        pluginCall.resolve();
                    }
                }
            );
        } catch (Exception e) {
            pluginCall.reject("SSLPinning found some problem with the request!", "2");
        }
    }

    private OkHttpClient.Builder getHttpClientBuilder() {
        OkHttpClient.Builder clientBuilder = OkHttpClientWrapper.getInstance().getOkHttpClient().newBuilder();
        ConnectionPool cP = new ConnectionPool(CON_MAX_IDLE_CONNECTIONS_DEFAULT, CON_KEEP_ALIVE_DEFAULT, TimeUnit.SECONDS);
        clientBuilder.connectionPool(cP);
        return clientBuilder;
    }
}
