/*!
 * watchmen
 *
 * Copyright 2013 OutSystems and other contributors
 * Released under the MIT license.
 */

export module watchmen {

    // declare 'stacktrace' submodule API for typescript compiler
    export module stacktrace {
        declare function printStackTrace(options: any): string[];

        export function getErrorStack(e: DOMException) {
            return stacktrace.printStackTrace({ e: e });
        }
    }

    // The logWriters module provides the ILogWriter interface and some implementations.
    // These implementations are capable of writing messages to many backends such as
    // the browser console, alert message boxes or even contacting a server.
    export module logWriters {
        export interface ILogWriter {
            writeMessage(message: string): void;
        }

        // Report message to the browser console if available.
        export class BrowserConsoleLogWriter implements ILogWriter {
            writeMessage(message: string) {
                window.console && console.log && console.log(message);
            }
        }

        // Report message as an alert() modal. Usage NOT RECOMMENDED.
        export class MessageBoxLogWriter implements ILogWriter {
            writeMessage(message: string) {
                alert(message);
            }
        }

        // Report messages as an alert() modal, but buffering them and issuing
        // a single alert() call after 100msec. This attempts to prevent the user
        // from being flooded by alert()s.
        export class MessageBoxAsyncLogWriter implements ILogWriter {
            pendingMessages = [];
            timerId = null;
            writeMessage(message: string) {
                this.pendingMessages[this.pendingMessages.length] = message;
                if (this.timerId === null) {
                    var self = this;
                    this.timerId = window.setTimeout(function () {
                        self.dump();
                        self.timerId = null;
                    }, 100)
                }
            }
            dump() {
                var msg = this.pendingMessages.join("\n");
                alert(msg);
                this.pendingMessages = [];
            }
        }

        // Reports messages to a new browser window.
        // This code wasn't really tested yet, so beware of using it.
        class DebugWindowWriter implements ILogWriter {
            writeMessage(message: string) {
                var hw = window.open("javascript:;", "debugWindowWriter", "", false);
                hw.document.open();
                hw.document.writeln("<pre>" +
                    message.replace(/</g, "&lt;")
                           .replace(/>/g, "&gt;")
                           .replace(/&/g, "&amp;") + "</pre>");
                hw.document.close();
            }
        }

        // Reports messages to a server endpoint.
        // Messages are reported using a synchronous HTTP request because if a message needs
        // reporting when the user is navigating away from the page, an asynchronous connection
        // would be canceled. As a result, calls to this method will be blocking and hinder
        // user experience. Use this only for problem reporting!
        export class ServerReporterWriter implements ILogWriter {
            rid = 0;
            baseurl = null;
            constructor (baseurl?: string) {
                if (baseurl) { this.baseurl = baseurl; }
            }
            writeMessage(message: string) {
                var urldata = "m=" + encodeURIComponent(message) +
                    "&ua=" + encodeURIComponent(navigator.userAgent) +
                    "&ur=" + encodeURIComponent(location.href) +
                    "&ex=" + encodeURIComponent((function (w:any) {
                        var ret = "<unavailable>";
                        var api = w.OSPlatform || w.outsystems;
                        if (!api) return ret;
                        api = api && api.internal && api.internal.requestInfo;
                        if (!api || !api.webScreenKey) return ret;
                        var atoms = [];
                        for (var k in api) {
                            if ((<Object>api).hasOwnProperty(k)) {
                                if (api[k]) {
                                    atoms.push(k + ":'" + (api[k]+"").replace(/'/g,"\\'") + "'");
                                }
                            }
                        }
                        return atoms.join(",");
                    })(window)) +
                    "&_=" + (+(new Date())) * 100 + this.rid++;
                var urlbase = (this.baseurl || "/watchmen/report.aspx");
                var req : XMLHttpRequest;
                if (typeof XMLHttpRequest !== "undefined") { req = new XMLHttpRequest() }
                else if (typeof ActiveXObject !== "undefined") { req = new ActiveXObject("Microsoft.XMLHTTP"); }
                else {
                    // can't find suitable ajax api
                    return;
                }
                req.open('POST', urlbase, false);
                req.setRequestHeader("Content-type","application/x-www-form-urlencoded");
                req.send(urldata);
                if (req.status < 200 || req.status >= 300) {
                    // POST failed?
                    // try a GET instead
                    req = new XMLHttpRequest();
                    req.open('GET', urlbase + "?" + urldata, false);
                    req.send(null);
                }
            }
        }
    }

    declare var OsHandleException: (exception: any, errorCode: number, origin: string) => void;

    // The Guardian provides the main API for logging and exception handling.
    class Guardian {
        writers : logWriters.ILogWriter[] = [];

        // Register a message writer for this guardian
        addWriter(writer: logWriters.ILogWriter) {
            this.writers[this.writers.length] = writer;
            return this;
        }

        // Log a message on all registered writers
        logMessage(message: string) {
            for (var i = 0; i < this.writers.length; i++) {
                this.writers[i].writeMessage(message);
            }
            return this;
        }

        // Log an exception on all available writers
        logException(e: DOMException) {
            var stack = stacktrace.getErrorStack(e);
            var msg = e.name + " - " + e.message + "\n" + stack.join("\n");
            this.logMessage(msg);
            return this;
        }

        // Monkey-patch OutSystems' platform exception handling routines
        attachToOSException() {
            if (typeof OsHandleException !== "undefined") {
                var self = this;
                var originalOsHandleException = OsHandleException;
                OsHandleException = function (ex, code, origin) {
                    self.logException(ex);
                    self.logMessage("Exception caught. Error code: " + code + "; origin: " + origin);
                    originalOsHandleException(ex, code, origin);
                };
            }
            return this;
        }

        // Attach to onerror global event handler
        attachToGlobalEvent() {
            var self = this;
            var oldOnError = window.onerror;
            window.onerror = function (eventOrMessage: any, source: string, fileno: number) {
                self.logMessage(eventOrMessage + " " + source + ":" + fileno);
                if (oldOnError) {
                    oldOnError.call(window, eventOrMessage, source, fileno);
                }
            }
            return this;
        }
    }

    var guardian: Guardian = null;
    export function getGuardian() {
        if (guardian === null) { guardian = new Guardian(); }
        return guardian;
    }
    export function auto() {
        return getGuardian()
            .addWriter(new logWriters.BrowserConsoleLogWriter())
            .addWriter(new logWriters.ServerReporterWriter())
            .attachToOSException()
            .attachToGlobalEvent();
    }
}
