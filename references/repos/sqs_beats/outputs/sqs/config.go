package sqs

import (
	"errors"
	"time"

	"github.com/elastic/beats/libbeat/common"
)

type sqsConfig struct {
	AccessKeyID     string `config:"access_key_id"`
	AccessSecretKey string `config:"access_secret_key"`
	AccessToken     string `config:"access_token"`
	Region          string `config:"region"`
	QueueURL        string `config:"queue_url"`
	BatchSize       int    `config:"batch_size"`
}

type backoff struct {
	Init time.Duration
	Max  time.Duration
}

const (
	defaultBatchSize = 1
	maxBatchSize     = 10
)

var (
	defaultConfig = sqsConfig{
		BatchSize: defaultBatchSize,
	}
)

func (c *sqsConfig) Validate() error {
	if c.AccessKeyID != "" && c.AccessSecretKey == "" {
		return errors.New("access_secret_key is not defined")
	}

	if c.Region == "" {
		return errors.New("region is not defined")
	}

	if c.QueueURL == "" {
		return errors.New("queue_url is not defined")
	}

	if c.BatchSize > maxBatchSize || c.BatchSize < 1 {
		return errors.New("invalid batch size")
	}

	return nil
}

// reads the configuration applying also the defaults
func readConfig(cfg *common.Config) (*sqsConfig, error) {
	c := defaultConfig
	if err := cfg.Unpack(&c); err != nil {
		return nil, err
	}
	return &c, nil
}
