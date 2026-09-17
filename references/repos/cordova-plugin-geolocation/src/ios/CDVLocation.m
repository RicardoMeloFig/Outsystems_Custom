/*
 Licensed to the Apache Software Foundation (ASF) under one
 or more contributor license agreements.  See the NOTICE file
 distributed with this work for additional information
 regarding copyright ownership.  The ASF licenses this file
 to you under the Apache License, Version 2.0 (the
 "License"); you may not use this file except in compliance
 with the License.  You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0

 Unless required by applicable law or agreed to in writing,
 software distributed under the License is distributed on an
 "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
 KIND, either express or implied.  See the License for the
 specific language governing permissions and limitations
 under the License.
 */

#import "CDVLocation.h"

#pragma mark Constants

#define kPGLocationErrorDomain @"kPGLocationErrorDomain"
#define kPGLocationDesiredAccuracyKey @"desiredAccuracy"
#define kPGLocationForcePromptKey @"forcePrompt"
#define kPGLocationDistanceFilterKey @"distanceFilter"
#define kPGLocationFrequencyKey @"frequency"

#pragma mark -
#pragma mark Categories

@implementation CDVLocationData

@synthesize locationStatus, locationInfo, locationCallbacks, watchCallbacks;
- (CDVLocationData*)init
{
    self = (CDVLocationData*)[super init];
    if (self) {
        self.locationInfo = nil;
        self.locationCallbacks = nil;
        self.watchCallbacks = nil;
    }
    return self;
}

@end

#pragma mark -
#pragma mark CDVLocation

@implementation CDVLocation

@synthesize locationManager, locationData;

- (void)pluginInitialize
{
    self.locationManager = [[CLLocationManager alloc] init];
    self.locationManager.delegate = self; // Tells the location manager to send updates to this object
    __locationStarted = NO;
    __highAccuracyEnabled = NO;
    self.locationData = nil;
}

- (BOOL)isAuthorized
{
    NSUInteger authStatus = [self.locationManager authorizationStatus];
    return (authStatus == kCLAuthorizationStatusAuthorizedWhenInUse) ||
           (authStatus == kCLAuthorizationStatusAuthorizedAlways) ||
           (authStatus == kCLAuthorizationStatusNotDetermined);
}

- (void)isLocationServicesEnabledWithCompletion:(void (^)(BOOL enabled))completion {
    if (!completion) {
        return;
    }

    dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0), ^{
        BOOL isEnabled = [CLLocationManager locationServicesEnabled];
        dispatch_async(dispatch_get_main_queue(), ^{
            completion(isEnabled);
        });
    });
}

- (void)startLocation:(BOOL)enableHighAccuracy
{
    __weak __typeof(self) weakSelf = self;
    
    [self isLocationServicesEnabledWithCompletion:^(BOOL enabled) {
        __strong __typeof(weakSelf) strongSelf = weakSelf;
        if (!strongSelf) {
            return;
        }
        
        if (!enabled) {
            [strongSelf returnLocationError:PERMISSIONDENIED withMessage:@"Location services are not enabled."];
            return;
        }
        
        if (![strongSelf isAuthorized]) {
            NSString* message = nil;
            BOOL authStatusAvailable = [CLLocationManager respondsToSelector:@selector(authorizationStatus)]; // iOS 4.2+
            if (authStatusAvailable) {
                NSUInteger code = [self.locationManager authorizationStatus];;
                if (code == kCLAuthorizationStatusNotDetermined) {
                    // could return POSITION_UNAVAILABLE but need to coordinate with other platforms
                    message = @"User undecided on application's use of location services.";
                } else if (code == kCLAuthorizationStatusRestricted) {
                    message = @"Application's use of location services is restricted.";
                }
            }
            // PERMISSIONDENIED is only PositionError that makes sense when authorization denied
            [strongSelf returnLocationError:PERMISSIONDENIED withMessage:message];

            return;
        }
        
        NSUInteger code = [strongSelf.locationManager authorizationStatus];
        
        if (code == kCLAuthorizationStatusNotDetermined ) {
            strongSelf->__highAccuracyEnabled = enableHighAccuracy;
            if([[NSBundle mainBundle] objectForInfoDictionaryKey:@"NSLocationWhenInUseUsageDescription"]){
                [strongSelf.locationManager requestWhenInUseAuthorization];
            } else if([[NSBundle mainBundle] objectForInfoDictionaryKey:@"NSLocationAlwaysUsageDescription"]) {
                [strongSelf.locationManager  requestAlwaysAuthorization];
            } else {
                NSLog(@"[Warning] No NSLocationAlwaysUsageDescription or NSLocationWhenInUseUsageDescription key is defined in the Info.plist file.");
            }
            return;
        }

        // Tell the location manager to start notifying us of location updates. We
        // first stop, and then start the updating to ensure we get at least one
        // update, even if our location did not change.
        [strongSelf.locationManager stopUpdatingLocation];
        [strongSelf.locationManager startUpdatingLocation];
        strongSelf->__locationStarted = YES;
        if (enableHighAccuracy) {
            strongSelf->__highAccuracyEnabled = YES;
            // Set distance filter to 5 for a high accuracy. Setting it to "kCLDistanceFilterNone" could provide a
            // higher accuracy, but it's also just spamming the callback with useless reports which drain the battery.
            strongSelf.locationManager.distanceFilter = 5;
            // Set desired accuracy to Best.
            strongSelf.locationManager.desiredAccuracy = kCLLocationAccuracyBest;
        } else {
            strongSelf->__highAccuracyEnabled = NO;
            strongSelf.locationManager.distanceFilter = 10;
            strongSelf.locationManager.desiredAccuracy = kCLLocationAccuracyThreeKilometers;
        }
    }];
}

- (void)_stopLocation
{
    if (__locationStarted) {
        __weak __typeof(self) weakSelf = self;
        
        [self isLocationServicesEnabledWithCompletion:^(BOOL enabled) {
            __strong __typeof(weakSelf) strongSelf = weakSelf;
            if (!strongSelf || !enabled) {
                return;
            }
            
            [strongSelf.locationManager stopUpdatingLocation];
            strongSelf->__locationStarted = NO;
            strongSelf->__highAccuracyEnabled = NO;
        }];
    }
}

- (void)locationManager:(CLLocationManager*)manager
    didUpdateToLocation:(CLLocation*)newLocation
           fromLocation:(CLLocation*)oldLocation
{
    CDVLocationData* cData = self.locationData;

    cData.locationInfo = newLocation;
    if (self.locationData.locationCallbacks.count > 0) {
        for (NSString* callbackId in self.locationData.locationCallbacks) {
            [self returnLocationInfo:callbackId andKeepCallback:NO];
        }

        [self.locationData.locationCallbacks removeAllObjects];
    }
    if (self.locationData.watchCallbacks.count > 0) {
        for (NSString* timerId in self.locationData.watchCallbacks) {
            [self returnLocationInfo:[self.locationData.watchCallbacks objectForKey:timerId] andKeepCallback:YES];
        }
    } else {
        // No callbacks waiting on us anymore, turn off listening.
        [self _stopLocation];
    }
}

- (void)getLocation:(CDVInvokedUrlCommand*)command
{
    [self.commandDelegate runInBackground:^{
        __weak __typeof(self) weakSelf = self;
        NSString* callbackId = command.callbackId;
        BOOL enableHighAccuracy = [[command argumentAtIndex:0] boolValue];

        [self isLocationServicesEnabledWithCompletion:^(BOOL enabled) {
            __strong __typeof(weakSelf) strongSelf = weakSelf;
            if (!strongSelf) {
                return;
            }
            
            if (enabled == NO) {
                [strongSelf returnLocationError:PERMISSIONDENIED withMessage:@"Location services are disabled."];
            } else {
                if (!strongSelf.locationData) {
                    strongSelf.locationData = [[CDVLocationData alloc] init];
                }
                CDVLocationData* lData = self.locationData;
                if (!lData.locationCallbacks) {
                    lData.locationCallbacks = [NSMutableArray arrayWithCapacity:1];
                }

                if (!strongSelf->__locationStarted || (strongSelf->__highAccuracyEnabled != enableHighAccuracy)) {
                    // add the callbackId into the array so we can call back when get data
                    if (callbackId != nil) {
                        [lData.locationCallbacks addObject:callbackId];
                    }
                    // Tell the location manager to start notifying us of heading updates
                    [strongSelf startLocation:enableHighAccuracy];
                } else {
                    [strongSelf returnLocationInfo:callbackId andKeepCallback:NO];
                }
            }
        }];
    }];
}

- (void)addWatch:(CDVInvokedUrlCommand*)command
{
    __weak __typeof(self) weakSelf = self;
    NSString* callbackId = command.callbackId;
    NSString* timerId = [command argumentAtIndex:0];
    BOOL enableHighAccuracy = [[command argumentAtIndex:1] boolValue];

    if (!self.locationData) {
        self.locationData = [[CDVLocationData alloc] init];
    }
    CDVLocationData* lData = self.locationData;

    if (!lData.watchCallbacks) {
        lData.watchCallbacks = [NSMutableDictionary dictionaryWithCapacity:1];
    }

    // add the callbackId into the dictionary so we can call back whenever get data
    [lData.watchCallbacks setObject:callbackId forKey:timerId];

    [self isLocationServicesEnabledWithCompletion:^(BOOL enabled) {
        __strong __typeof(weakSelf) strongSelf = weakSelf;
        if (!strongSelf) {
            return;
        }
        
        if (enabled == NO) {
            [strongSelf returnLocationError:PERMISSIONDENIED withMessage:@"Location services are disabled."];
        } else if (!strongSelf->__locationStarted || (strongSelf->__highAccuracyEnabled != enableHighAccuracy)) {
            // Tell the location manager to start notifying us of location updates
            [strongSelf startLocation:enableHighAccuracy];
        }
    }];
}

- (void)clearWatch:(CDVInvokedUrlCommand*)command
{
    NSString* timerId = [command argumentAtIndex:0];

    if (self.locationData && self.locationData.watchCallbacks && [self.locationData.watchCallbacks objectForKey:timerId]) {
        [self.locationData.watchCallbacks removeObjectForKey:timerId];
        if([self.locationData.watchCallbacks count] == 0) {
            [self _stopLocation];
        }
    }
}

- (void)stopLocation:(CDVInvokedUrlCommand*)command
{
    [self _stopLocation];
}

- (void)returnLocationInfo:(NSString*)callbackId andKeepCallback:(BOOL)keepCallback
{
    CDVPluginResult* result = nil;
    CDVLocationData* lData = self.locationData;

    if (lData && !lData.locationInfo) {
        // return error
        result = [CDVPluginResult resultWithStatus:CDVCommandStatus_ERROR messageToErrorObject:POSITIONUNAVAILABLE];
    } else if (lData && lData.locationInfo) {
        CLLocation* lInfo = lData.locationInfo;
        NSMutableDictionary* returnInfo = [NSMutableDictionary dictionaryWithCapacity:8];
        NSNumber* timestamp = [NSNumber numberWithDouble:([lInfo.timestamp timeIntervalSince1970] * 1000)];
        [returnInfo setObject:timestamp forKey:@"timestamp"];
        [returnInfo setObject:[NSNumber numberWithDouble:lInfo.speed] forKey:@"velocity"];
        [returnInfo setObject:[NSNumber numberWithDouble:lInfo.verticalAccuracy] forKey:@"altitudeAccuracy"];
        [returnInfo setObject:[NSNumber numberWithDouble:lInfo.horizontalAccuracy] forKey:@"accuracy"];
        [returnInfo setObject:[NSNumber numberWithDouble:lInfo.course] forKey:@"heading"];
        [returnInfo setObject:[NSNumber numberWithDouble:lInfo.altitude] forKey:@"altitude"];
        
        //Set maximum decimal digits to 15 for JS float compatibility
        NSNumberFormatter *formatter = [[NSNumberFormatter alloc] init];
        [formatter setMaximumFractionDigits:15];
        [formatter setRoundingMode: NSNumberFormatterRoundUp];
        NSString* latitude = [formatter stringFromNumber:[NSNumber numberWithDouble:lInfo.coordinate.latitude]];
        NSString* longitude = [formatter stringFromNumber:[NSNumber numberWithDouble:lInfo.coordinate.longitude]];
        
        [returnInfo setObject:[formatter numberFromString: latitude] forKey:@"latitude"];
        [returnInfo setObject:[formatter numberFromString: longitude] forKey:@"longitude"];

        result = [CDVPluginResult resultWithStatus:CDVCommandStatus_OK messageAsDictionary:returnInfo];
        [result setKeepCallbackAsBool:keepCallback];
    }
    if (result) {
        [self.commandDelegate sendPluginResult:result callbackId:callbackId];
    }
}

- (void)returnLocationError:(NSUInteger)errorCode withMessage:(NSString*)message
{
    NSMutableDictionary* posError = [NSMutableDictionary dictionaryWithCapacity:2];

    [posError setObject:[NSNumber numberWithUnsignedInteger:errorCode] forKey:@"code"];
    [posError setObject:message ? message:@"" forKey:@"message"];
    CDVPluginResult* result = [CDVPluginResult resultWithStatus:CDVCommandStatus_ERROR messageAsDictionary:posError];

    for (NSString* callbackId in self.locationData.locationCallbacks) {
        [self.commandDelegate sendPluginResult:result callbackId:callbackId];
    }

    [self.locationData.locationCallbacks removeAllObjects];

    for (NSString* callbackId in self.locationData.watchCallbacks) {
        [self.commandDelegate sendPluginResult:result callbackId:callbackId];
    }
}

- (void)locationManager:(CLLocationManager*)manager didFailWithError:(NSError*)error
{
    NSLog(@"locationManager::didFailWithError %@", [error localizedFailureReason]);

    CDVLocationData* lData = self.locationData;
    if (lData && __locationStarted) {
        // TODO: probably have to once over the various error codes and return one of:
        // PositionError.PERMISSION_DENIED = 1;
        // PositionError.POSITION_UNAVAILABLE = 2;
        // PositionError.TIMEOUT = 3;
        NSUInteger positionError = POSITIONUNAVAILABLE;
        if (error.code == kCLErrorDenied) {
            positionError = PERMISSIONDENIED;
        }
        [self returnLocationError:positionError withMessage:[error localizedDescription]];
    }

    if (error.code != kCLErrorLocationUnknown) {
      [self.locationManager stopUpdatingLocation];
      __locationStarted = NO;
    }
}

//iOS8+
-(void)locationManager:(CLLocationManager *)manager didChangeAuthorizationStatus:(CLAuthorizationStatus)status
{
    if(!__locationStarted){
        [self startLocation:__highAccuracyEnabled];
    }
}

- (void)dealloc
{
    self.locationManager.delegate = nil;
}

- (void)onReset
{
    [self _stopLocation];
    [self.locationManager stopUpdatingHeading];
}

@end
