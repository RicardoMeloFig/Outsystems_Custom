//
//  OSECTProvider.h
//  OutSystemsMobileECT
//
//  Created by engineering on 06/02/2017.
//  Copyright © 2017 OutSystems. All rights reserved.
//

#ifndef OutSystemsMobileECT_OSECTProvider_h
#define OutSystemsMobileECT_OSECTProvider_h

#import <Foundation/Foundation.h>
#import <OutSystemsMobileECT/OSECTAppInfo.h>

typedef void (^OSECTRequestHandlerBlock)(NSDictionary *jsonResult, NSError* error, NSNumber* statusCode);

@protocol OSECTProvider <NSObject>
@required
-(void)getCurrentAPIVersion:(NSString *)hostname completionBlock:(OSECTRequestHandlerBlock)completionBlock;
-(void)getAppFeedbackSettingsForApplication:(OSECTAppInfo*)appInfo hostname:(NSString *)hostname  completionBlock:(OSECTRequestHandlerBlock)completionBlock;
-(void)submitFeedbackForApplication:(OSECTAppInfo*)appInfo hostname:(NSString *)hostname  completionBlock:(OSECTRequestHandlerBlock)completionBlock;
@end

#endif
