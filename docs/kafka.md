# Kafka

The Kafka broker, topic definitions, consumer groups and hosted-broker provisioning for all
Wonrich services live in the shared **wonrich-infra** repository:

- Topic definitions: `wonrich-infra/kafka/topics.env`
- Topic creation: `wonrich-infra/kafka/create-topics.sh`
- Conventions, topic and consumer group tables: `wonrich-infra/docs/kafka.md`
- Hosted broker: `wonrich-infra/azure/eventhubs.sh`

This file was moved there so every service shares one definition (originally SCRUM-88).
