//
//  OSECTAppInfo.h
//  OutSystemsMobileECT
//
//  Created by engineering on 06/02/2017.
//  Copyright © 2017 OutSystems. All rights reserved.
//

#ifndef OutSystemsMobileECT_OSECTAppInfo_h
#define OutSystemsMobileECT_OSECTAppInfo_h

#import <Foundation/Foundation.h>
#import <OutSystemsMobileECT/OSECTSupportedAPIVersions.h>

extern NSString* const OSECTAPIWebApp;
extern NSString* const OSECTAPIMobileApp;
extern NSString* const OSECTAPIInvalidApp;

@interface OSECTAppInfo : NSObject

@property (nonatomic, retain) NSString* ApplicationKind;
@property (nonatomic) BOOL ECTAvailable;
@property (nonatomic, retain) NSString* EnvironmentKey;
@property (nonatomic, retain) NSString* EspaceKey;
@property (nonatomic, retain) NSString* ApplicationKey;
@property (nonatomic, retain) NSString* ScreenKey;
@property (nonatomic, retain) NSString* ScreenName;
@property (nonatomic, retain) NSNumber* UserId;
@property (nonatomic, retain) NSString* ViewportWidth;
@property (nonatomic, retain) NSString* ViewportHeight;
@property (nonatomic, retain) NSString* UserAgentHeader;
@property (nonatomic, retain) NSString* NavigatorAppName;
@property (nonatomic, retain) NSString* NavigatorAppVersion;
@property (nonatomic, retain) NSString* OperatingSystem;
@property (nonatomic, retain) NSString* OperatingSystemVersion;
@property (nonatomic, retain) NSString* DeviceModel;
@property (nonatomic, retain) NSString* RequestURL;
@property (nonatomic, retain) NSString* FeedbackSoundMessageBase64;
@property (nonatomic, retain) NSString* FeedbackSoundMessageMimeType;
@property (nonatomic, retain) NSString* FeedbackScreenshotBase64;
@property (nonatomic, retain) NSString* FeedbackScreenshotMimeType;
@property (nonatomic, retain) NSString* Message;

@property (retain, nonatomic) OSECTSupportedAPIVersions *ectSupportedApiVersions;


-(id)initWithJson:(NSDictionary*)json;

@end

#endif
