package com.evothings;

import com.estimote.sdk.Region;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.UUID;

/**
 * Created by João Gonçalves (jppg) on 04/03/16.
 */
public class JSONUtils {

    /**
     * @param region
     * @return JSONObject representing the given region or null if the region is null.
     * @throws JSONException
     */
    public static JSONObject toJSONObject(Region region) throws JSONException {
        if (region == null) {
            return null;
        }
        JSONObject jsonObject = new JSONObject();
        jsonObject.put("identifier", region.getIdentifier());
        jsonObject.put("uuid", region.getProximityUUID());
        jsonObject.put("major", region.getMajor());
        jsonObject.put("minor", region.getMinor());
        if (region instanceof NotificationRegion) {
            final NotificationRegion notificationRegion = (NotificationRegion) region;
            jsonObject.put("enterTitle", notificationRegion.getEnterTitle());
            jsonObject.put("enterMessage", notificationRegion.getEnterMessage());
            jsonObject.put("exitTitle", notificationRegion.getExitTitle());
            jsonObject.put("exitMessage", notificationRegion.getExitMessage());
            jsonObject.put("deeplink", notificationRegion.getDeeplink());
            jsonObject.put("openedFromNotification", notificationRegion.isOpenedFromNotification());
            jsonObject.put("idle", notificationRegion.getIdle());
        }
        return jsonObject;
    }

    /**
     * @param region
     * @param state
     * @return JSONObject representing the given region with the associated state or null if the region is null
     * @throws JSONException
     */
    public static JSONObject toJSONObject(Region region, String state) throws JSONException {
        JSONObject jsonObject = JSONUtils.toJSONObject(region);
        if (jsonObject != null && state != null) {
            jsonObject.put("state", state);
        }
        return jsonObject;
    }

    /**
     * @param region
     * @return JSON string representing the given region or null if the region is null
     */
    public static String toJson(Region region) {
        try {
            final JSONObject obj = JSONUtils.toJSONObject(region);
            if (obj != null) {
                return obj.toString();
            } else {
                return null;
            }
        } catch (JSONException e) {
            return null;
        }
    }

    /**
     * @param region
     * @return JSON string representing the given region or null if the region is null
     */
    public static String toDbJson(Region region) {
        try {
            final JSONObject obj = JSONUtils.toJSONObject(region);
            if(region instanceof NotificationRegion) {
                obj.put("lastNotificationTime", ((NotificationRegion)region).getLastNotificationTime());
            }
            if (obj != null) {
                return obj.toString();
            } else {
                return null;
            }
        } catch (JSONException e) {
            return null;
        }
    }

    /**
     * @param json
     * @return an instance of Region given the JSON
     */
    public static Region fromJson(String json) {
        try {
            JSONObject obj = new JSONObject(json);

            // Identifier

            String identifier = obj.optString("identifier");

            String tmpProximityUUID = obj.isNull("uuid") ? null : obj.optString("uuid", null);

            // Proximity UUID

            UUID proximityUUID = null;
            if (tmpProximityUUID != null && !tmpProximityUUID.isEmpty()) {
                proximityUUID = UUID.fromString(tmpProximityUUID);
            }

            // Major

            int tmpMajor = obj.isNull("major") ? -1 : obj.optInt("major", -1);
            Integer major = null;
            if (tmpMajor != -1)
                major = tmpMajor;

            // Minor

            int tmpMinor = obj.isNull("minor") ? -1 : obj.optInt("minor", -1);
            Integer minor = null;
            if (tmpMinor != -1)
                minor = tmpMinor;

            String enterTitleTmp = obj.optString("enterTitle", "");
            String enterMessageTmp = obj.optString("enterMessage", "");
            String exitTitleTmp = obj.optString("exitTitle", "");
            String exitMessageTmp = obj.optString("exitMessage", "");
            String deeplink = obj.optString("deeplink", "");
            int idle = obj.optInt("idle", 0);
            long lastNotificationTime = obj.optLong("lastNotificationTime", 0);

            if (identifier != null && identifier.isEmpty()) {
                identifier = JSONUtils.regionHashMapKey(proximityUUID, major, minor);
            }

            if (enterTitleTmp.isEmpty() && enterMessageTmp.isEmpty() &&
                    exitTitleTmp.isEmpty() && exitMessageTmp.isEmpty()) {
                return new Region(identifier, proximityUUID, major, minor);
            } else {
                NotificationRegion ret = new NotificationRegion(identifier, proximityUUID, major, minor, enterMessageTmp, enterTitleTmp, exitMessageTmp, exitTitleTmp, deeplink, idle);
                ret.setLastNotificationTime(lastNotificationTime);
                return ret;
            }
        } catch (JSONException e) {
            e.printStackTrace();
            return null;
        }
    }

    private static String regionHashMapKey(UUID uuid, Integer major, Integer minor) {
        String tmpUuid = null;
        if (uuid == null) {
            tmpUuid = "0";
        } else {
            tmpUuid = uuid.toString();
        }

        if (major == null) {
            major = 0;
        }

        if (minor == null) {
            minor = 0;
        }

        // use % for easier decomposition
        return tmpUuid + "%" + major + "%" + minor;
    }

    public static String EscapeJavaScriptFunctionParameter(String param) {
        char[] chars = JSONObject.quote(param).toCharArray();

        StringBuilder sb = new StringBuilder();

        for (int i = 0; i < chars.length; i++) {
            switch (chars[i]) {
                case '%':
                    sb.append("\\%");
                    break;
                default:
                    sb.append(chars[i]);
                    break;
            }

        }
        return sb.toString();
    }
}
