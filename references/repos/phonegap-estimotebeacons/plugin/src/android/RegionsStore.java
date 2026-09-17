package com.evothings;

import android.content.Context;

import com.estimote.sdk.Region;

import java.util.ArrayList;
import java.util.List;

/**
 * Created by João Gonçalves (jppg) on 03/03/16.
 */
public class RegionsStore {

    private final static String TAG = "RegionsStore";

    private LocalStorage storage;

    public RegionsStore(Context context) {
        this.storage = new LocalStorage(context);
    }

    public void setRegion(Region region) {
        String jsonRegion = JSONUtils.toDbJson(region);
        storage.setItem(region.getIdentifier(), jsonRegion);
    }

    public Region getRegion(String id) {
        String json = storage.getItem(id);
        if(json == null) {
            return null;
        }
        else {

            return JSONUtils.fromJson(json);
        }
    }

    public List<Region> getAll() {
        List<String> jsonObjList = storage.getAllItems();
        List<Region> result = new ArrayList<Region>();
        for (String json : jsonObjList) {
            result.add(JSONUtils.fromJson(json));
        }
        return result;
    }

    public void remove(String id) {
        storage.removeItem(id);
    }

    public void clear() {
        storage.clear();
    }
}
