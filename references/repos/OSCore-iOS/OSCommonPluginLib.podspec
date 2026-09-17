Pod::Spec.new do |s|
  s.name             = 'OSCommonPluginLib'
  s.version          = '1.0.0'
  s.summary          = 'Provides an interface between the business logic and the iOS platform. It can be used for integrations with existing mobile frameworks.'
  s.description      = <<-DESC
This repository contains a protocol that interfaces business logic with iOS platform.
Developers are able to create their own integrations of PlatformProtocol for their target mobile framework.
This structure allows us to develop native plugins, portable between techonological stacks (ie: Cordova, React-native, Flutter...).
The library is composed by two methods, each with its own purpose:
- `sendResult(result, error, callBackID)` will send messages back to the caller mobile framework.
- `trigger(event, data)` will trigger an event on the mobile framework's own webview.
                       DESC
                       
  s.homepage         = 'https://github.com/OutSystems/OSCore-iOS'
  s.license          = { :type => 'MIT', :file => 'LICENSE' }
  s.author           = { 'Mobile Ecosystem Team' => 'rd.mobileecosystem.team@outsystems.com' }
  s.source           = { :git => 'https://github.com/OutSystems/OSCore-iOS.git', :tag => s.version.to_s }

  s.ios.deployment_target = '13.0'
  s.swift_versions = '5.0'

  s.source_files = 'OSCore/**/*.swift'
end