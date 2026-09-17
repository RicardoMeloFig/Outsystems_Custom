package com.os.mobile.cachelocation;

/**
 * Created by vitoroliveira on 03/08/15.
 */
public class LocationStore {

    private String latitude;

    private String longitude;

    private String timeStamp;

    private static LocationStore instance = null;

    protected LocationStore() {
        // Exists only to defeat instantiation.
    }

    public static LocationStore getInstance() {
        if (instance == null) {
            instance = new LocationStore();
        }
        return instance;
    }

    public void setNewLocation (String mLatitude, String mLongitude, String mTimeStamp) {
        this.latitude = mLatitude;
        this.longitude = mLongitude;
        this.timeStamp = mTimeStamp;
    }

    public String getLatitude() {
        return latitude;
    }

    public void setLatitude(String latitude) {
        this.latitude = latitude;
    }

    public String getLongitude() {
        return longitude;
    }

    public void setLongitude(String longitude) {
        this.longitude = longitude;
    }

    public String getTimeStamp() {
        return timeStamp;
    }

    public void setTimeStamp(String timeStamp) {
        this.timeStamp = timeStamp;
    }
}
