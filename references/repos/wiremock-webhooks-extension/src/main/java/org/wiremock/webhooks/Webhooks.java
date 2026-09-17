package org.wiremock.webhooks;

import com.github.tomakehurst.wiremock.common.Notifier;
import com.github.tomakehurst.wiremock.core.Admin;
import com.github.tomakehurst.wiremock.extension.Parameters;
import com.github.tomakehurst.wiremock.extension.PostServeAction;
import com.github.tomakehurst.wiremock.http.HttpClientFactory;
import com.github.tomakehurst.wiremock.http.HttpHeader;
import com.github.tomakehurst.wiremock.stubbing.ServeEvent;
import wiremock.org.apache.http.HttpResponse;
import wiremock.org.apache.http.client.HttpClient;
import wiremock.org.apache.http.client.methods.HttpEntityEnclosingRequestBase;
import wiremock.org.apache.http.client.methods.HttpUriRequest;
import wiremock.org.apache.http.entity.ByteArrayEntity;
import wiremock.org.apache.http.util.EntityUtils;
import org.wiremock.webhooks.interceptors.WebhookTransformer;

import java.io.IOException;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;

import static com.github.tomakehurst.wiremock.common.Exceptions.throwUnchecked;
import static com.github.tomakehurst.wiremock.common.LocalNotifier.notifier;
import static com.github.tomakehurst.wiremock.http.HttpClientFactory.getHttpRequestFor;
import static java.util.concurrent.TimeUnit.SECONDS;

public class Webhooks extends PostServeAction {

    private final ScheduledExecutorService scheduler;
    private final HttpClient httpClient;
    private final List<WebhookTransformer> transformers;

    private Webhooks(
            ScheduledExecutorService scheduler,
            HttpClient httpClient,
            List<WebhookTransformer> transformers) {
      this.scheduler = scheduler;
      this.httpClient = httpClient;
      this.transformers = transformers;
    }

    public Webhooks() {
      this(Executors.newScheduledThreadPool(10), HttpClientFactory.createClient(), new ArrayList<WebhookTransformer>());
    }

    public Webhooks(WebhookTransformer... transformers) {
      this(Executors.newScheduledThreadPool(10), HttpClientFactory.createClient(), Arrays.asList(transformers));
    }

    @Override
    public String getName() {
        return "webhook";
    }

    @Override
    public void doAction(final ServeEvent serveEvent, final Admin admin, final Parameters parameters) {
        final Notifier notifier = notifier();

        notifier.info("doAction on Webhooks");

        WebhookDefinition definition = parameters.as(WebhookDefinition.class);
        for (WebhookTransformer transformer: transformers) {
            definition = transformer.transform(serveEvent, definition);
        }

        notifier.info("Delay in seconds is " + definition.getDelayInSeconds());
        Long delayInSeconds = 
            definition.getDelayInSeconds() == null || definition.getDelayInSeconds().isEmpty()
            ? 0L
            : Long.valueOf(definition.getDelayInSeconds());

        HttpUriRequest request = buildRequest(definition);

        notifier.info("Scheduling...");
        scheduler.schedule(
            (Runnable)(new WebhookRunner(definition, request, this.httpClient, notifier)),
            delayInSeconds.longValue(),
            SECONDS
        );
    }

    private static HttpUriRequest buildRequest(WebhookDefinition definition) {
        HttpUriRequest request = getHttpRequestFor(
                definition.getMethod(),
                definition.getUrl().toString()
        );

        for (HttpHeader header: definition.getHeaders().all()) {
            request.addHeader(header.key(), header.firstValue());
        }

        if (definition.getMethod().hasEntity()) {
            HttpEntityEnclosingRequestBase entityRequest = (HttpEntityEnclosingRequestBase) request;
            entityRequest.setEntity(new ByteArrayEntity(definition.getBinaryBody()));
        }

        return request;
    }

    public static WebhookDefinition webhook() {
        return new WebhookDefinition();
    }
}


class WebhookRunner implements Runnable {

    private WebhookDefinition definition;
    private Notifier notifier;
    private HttpClient httpClient;
    private HttpUriRequest request;

    public WebhookRunner(WebhookDefinition definition, HttpUriRequest request, HttpClient httpClient, Notifier notifier) {
        this.definition = definition;
        this.request = request;
        this.httpClient = httpClient;
        this.notifier = notifier;
    }

    @Override
    public void run() {
        this.notifier.info("Running WebhookRunner");
        try {
            HttpResponse response = this.httpClient.execute(this.request);
            this.notifier.info(
                String.format("Webhook %s request to %s returned status %s\n\n%s",
                    this.definition.getMethod(),
                    this.definition.getUrl(),
                    response.getStatusLine(),
                    EntityUtils.toString(response.getEntity())
                )
            );
        } catch (IOException e) {
            this.notifier.info("Error: " + e.getMessage());
            throwUnchecked(e);
        }
    }
}
