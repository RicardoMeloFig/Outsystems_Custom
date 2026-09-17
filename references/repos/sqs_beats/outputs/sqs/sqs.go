package sqs

import (
	"github.com/elastic/beats/libbeat/beat"
	"github.com/elastic/beats/libbeat/common"
	"github.com/elastic/beats/libbeat/outputs"
)

// registers the output
func init() {
	outputs.RegisterType("sqs", makeSqs)
}

// creates the SQS output
func makeSqs(
	_ outputs.IndexManager,
	beat beat.Info,
	observer outputs.Observer,
	cfg *common.Config,
) (outputs.Group, error) {

	config, err := readConfig(cfg)
	if err != nil {
		return outputs.Fail(err)
	}

	client, err := newClient(config, observer, beat)
	if err != nil {
		return outputs.Fail(err)
	}

	return outputs.Success(config.BatchSize, -1, client)
}
