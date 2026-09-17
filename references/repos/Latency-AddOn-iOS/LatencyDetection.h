//
//  LatencyDetection.h
//  OutSystems
//
//  Created by Vitor Oliveira on 03/11/15.
//
//

#import <Foundation/Foundation.h>

@interface LatencyDetection : NSObject

+(void)pingHostname:(NSString*)hostName andResultCallback:(void(^)(NSString* latency))result;

@end
