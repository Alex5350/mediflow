name: Bug report
description: Something is broken
labels: [bug]
body:
  - type: textarea
    attributes:
      label: What happened?
      description: Include logs (console output, worker log, claim number) where relevant.
    validations:
      required: true
  - type: textarea
    attributes:
      label: Steps to reproduce
    validations:
      required: true
