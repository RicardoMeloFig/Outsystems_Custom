package main

import (
	"bytes"
	"encoding/json"
)

// Represents a non-root node in a notification routing tree.
type SpecificPolicy struct {
	Receiver          string                 `json:"receiver,omitempty"`
	GroupBy           []string               `json:"group_by,omitempty"`
	ObjectMatchers    map[string]interface{} `json:"object_matchers,omitempty"`
	MuteTimeIntervals []string               `json:"mute_time_intervals,omitempty"`
	Continue          bool                   `json:"continue,omitempty"`
	Routes            interface{}            `json:"routes,omitempty"`
	GroupWait         int64                  `json:"group_wait,omitempty"`
	GroupInterval     int64                  `json:"group_interval,omitempty"`
	RepeatInterval    int64                  `json:"repeat_interval,omitempty"`
	Provenance        string                 `json:"Provenance,omitempty"`
}

// NotificationPolicy fetches the notification policy tree.
func (c *Client) NotificationPolicyTree() (SpecificPolicy, error) {
	np := SpecificPolicy{}
	err := c.request("GET", "/api/v1/provisioning/policies", nil, nil, &np)
	return np, err
}

// SetNotificationPolicy sets the notification policy tree.
func (c *Client) SetNotificationPolicyTree(np *SpecificPolicy) error {
	req, err := json.Marshal(np)
	if err != nil {
		return err
	}
	return c.request("PUT", "/api/v1/provisioning/policies", nil, bytes.NewBuffer(req), nil)
}

func (c *Client) ResetNotificationPolicyTree() error {
	return c.request("DELETE", "/api/v1/provisioning/policies", nil, nil, nil)
}
