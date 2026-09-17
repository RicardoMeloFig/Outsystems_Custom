package com.outsystems.plugins.inappbrowser.osinappbrowser

import com.google.gson.annotations.SerializedName
import com.outsystems.plugins.inappbrowser.osinappbrowserlib.models.OSIABAnimation
import com.outsystems.plugins.inappbrowser.osinappbrowserlib.models.OSIABBottomSheet
import com.outsystems.plugins.inappbrowser.osinappbrowserlib.models.OSIABViewStyle

data class OSInAppBrowserSystemBrowserInputArguments(
    @SerializedName("android") val android: OSInAppBrowserSystemBrowserAndroidOptions?
)

data class OSInAppBrowserSystemBrowserAndroidOptions(
    @SerializedName("showTitle") val showTitle: Boolean?,
    @SerializedName("hideToolbarOnScroll") val hideToolbarOnScroll: Boolean?,
    @SerializedName("viewStyle") val viewStyle: OSIABViewStyle?,
    @SerializedName("bottomSheetOptions") val bottomSheetOptions: OSIABBottomSheet?,
    @SerializedName("startAnimation") val startAnimation: OSIABAnimation?,
    @SerializedName("exitAnimation") val exitAnimation: OSIABAnimation?,
)