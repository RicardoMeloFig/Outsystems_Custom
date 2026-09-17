//
//  GeofenceHelper.h
//  OutSystems
//
//  Created by Vitor Oliveira on 20/04/16.
//
//

#import <Foundation/Foundation.h>

@interface GeofenceHelper : NSObject

+(BOOL) validateTimeIntervalWithString: (NSString*) geofenceStr;
+(BOOL) validateTimeIntervalWithDictionary: (NSDictionary *) parsedData;

@end
