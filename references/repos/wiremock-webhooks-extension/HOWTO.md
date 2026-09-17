# How to compile this module


## Prerequirements
1. Install JDK 1.7
2. Install Gradle 4.8.1


## Define the following variables

```
export JAVA_HOME=<the Java 1.7 SDK path>
export GRADLE_PATH=<the Grale 4.8.1 path>
export PATH=$JAVA_HOME/bin:$GRADLE_PATH/bin:$PATH

```

## Test the versions

```
$ java -version

java version "1.7.0_80"
Java(TM) SE Runtime Environment (build 1.7.0_80-b15)
Java HotSpot(TM) 64-Bit Server VM (build 24.80-b11, mixed mode)

$ gradle -version

------------------------------------------------------------
Gradle 4.8.1
------------------------------------------------------------

Build time:   2018-06-21 07:53:06 UTC
Revision:     0abdea078047b12df42e7750ccba34d69b516a22

Groovy:       2.4.12
Ant:          Apache Ant(TM) version 1.9.11 compiled on March 23 2018
JVM:          1.7.0_80 (Oracle Corporation 24.80-b11)
OS:           Linux 5.4.0-66-generic amd64
```

## Build run:

```
gradle build
```
