package main

import (
	"os"

	"github.com/elastic/beats/winlogbeat/cmd"
	_ "github.com/renato0307/sqs_beats/outputs/sqs"
)

func main() {
	if err := cmd.RootCmd.Execute(); err != nil {
		os.Exit(1)
	}
}
