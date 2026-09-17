# kud
A set of helper scripts and Helm values files to standup a local Kubernetes clusters based on K0s.

## Background
As I've been learning Kubernetes, I've been using k0s to teach myself the various components. As a
security professional by day, I wanted to align my deployments with what I think are good practices
around security, access, etc. **At this stage, it may be incorrect. I am still learning.** As such,
this will continue to evolve as I learn more.

## Requirements
### *(and, let's be honest, a couple limitations)*
- Helm 3
- Hosts file updated to point k0s.local and www.k0s.local to 127.0.0.1

## Current Status
kud will deploy a basic local cluster with an ingress controller and external load balancer.

## Current Features
### Basic Cluster
- one controller and *n* worker containers up
- external load balancer config dynamically generated
- external load balancer deployed with config
- .kube/config extracted from controller and placed in ~/.kube/config
- nginx-ingress deployed

## Feature Progress
All progress and planned features can be tracked [here](https://github.com/mountainerd/kud/projects/1).

## Issues
If there are issues, please open an issue and/or fork and submit a PR for improvements.

## TODO
