package main

import (
	"encoding/json"
	"fmt"
	"io"
	"io/ioutil"
	"net/http"
	"net/url"
	"path"
	"time"
)

// Client is a Grafana API client.
type Client struct {
	baseURL url.URL
	token   string
	client  *http.Client
}

func NewClient(baseURL string, token string) (*Client, error) {

	u, err := url.Parse(baseURL)
	if err != nil {
		return nil, err
	}

	return &Client{
		baseURL: *u,
		token:   token,
		client: &http.Client{
			Timeout: time.Minute,
		},
	}, nil
}

func (c *Client) request(method, requestPath string, query url.Values, body io.Reader, responseStruct interface{}) error {
	var (
		req          *http.Request
		resp         *http.Response
		err          error
		bodyContents []byte
	)

	// retry logic
	for n := 0; n <= 2; n++ {
		req, err = c.newRequest(method, requestPath, query, body)
		if err != nil {
			return err
		}
		// Wait a bit if that's not the first request
		if n != 0 {
			time.Sleep(time.Second * 5)
		}
		resp, err = c.client.Do(req)
		// If err is not nil, retry again
		// That's either caused by client policy, or failure to speak HTTP (such as network connectivity problem). A
		// non-2xx status code doesn't cause an error.
		if err != nil {
			continue
		}
		defer resp.Body.Close()
		// read the body (even on non-successful HTTP status codes), as that's what the unit tests expect
		bodyContents, err = ioutil.ReadAll(resp.Body)
		// if there was an error reading the body, try again
		if err != nil {
			continue
		}
		// Exit the loop if we have something final to return. This is anything < 500, if it's not a 429.
		if resp.StatusCode < http.StatusInternalServerError && resp.StatusCode != http.StatusTooManyRequests {
			break
		}
	}
	if err != nil {
		return err
	}
	// check status code.
	if resp.StatusCode >= 400 {
		return fmt.Errorf("status: %d, body: %v", resp.StatusCode, string(bodyContents))
	}
	if responseStruct == nil {
		return nil
	}
	err = json.Unmarshal(bodyContents, responseStruct)
	if err != nil {
		return err
	}
	return nil
}

func (c *Client) newRequest(method, requestPath string, query url.Values, body io.Reader) (*http.Request, error) {
	url := c.baseURL
	url.Path = path.Join(url.Path, requestPath)
	url.RawQuery = query.Encode()
	req, err := http.NewRequest(method, url.String(), body)
	if err != nil {
		return req, err
	}
	req.Header.Add("Authorization", c.token)
	req.Header.Add("Content-Type", "application/json")
	return req, err
}
