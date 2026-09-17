package com.outsystems.latency;

/**
 * Created by vmfo on 30/03/15.
 */
public interface LatencyResult {

    public void onSuccessResult(String result);

    public void onErrorResult(String error);

}
