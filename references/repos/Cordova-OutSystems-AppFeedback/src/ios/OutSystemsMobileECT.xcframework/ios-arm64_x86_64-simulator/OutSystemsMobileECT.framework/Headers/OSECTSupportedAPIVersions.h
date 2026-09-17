//
//  OSECTSupportedAPIVersions.h
//  OutSystemsMobileECT
//
//  Created by engineering on 12/11/14.
//  Copyright (c) 2014 OutSystems. All rights reserved.
//

#ifndef OutSystemsMobileECT_OSECTSupportedAPIVersions_h
#define OutSystemsMobileECT_OSECTSupportedAPIVersions_h

#import <UIKit/UIKit.h>
#import <OutSystemsMobileECT/OSECTApi.h>

@interface OSECTSupportedAPIVersions : NSObject

@property (retain, nonatomic) NSMutableArray *supportedApiVersions;

-(id)init;

-(void)addVersion:(OSECTApi*) api;
-(void)removeAllVersions;

-(BOOL)checkCompatibilityWithVersion:(NSString *)version;
-(NSString*)getAPIVersionURL;

@end

#endif
