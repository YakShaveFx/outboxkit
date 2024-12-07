# Simple end-to-end sample of OutboxKit.MySql.Polling usage

(in `MySqlEndToEndPollingSample` folder)

Start everything with Docker compose, with one of two options:

1. using in process producer

```sh
ENABLE_OUT_OF_PROCESS_PRODUCER=false docker compose up -d --build
```

2. using out of process producer

```sh
ENABLE_OUT_OF_PROCESS_PRODUCER=true docker compose up -d --build
```

Open Grafana (`http://localhost:3000`) and create a new dashboard, importing the file `grafana-dashboard.json` from this folder.

Open RabbitMQ management UI, at `http://localhost:15672` (user:pass = guest:guest), to check has messages are sent.

Hammer the API using k6:

```sh
k6 run --vus 100 --duration 5m k6-script.js
```

Running things on a laptop is not very representative of real-world scenarios, so the following numbers are mostly for the fun of it.

With this setup, on my laptop, got between 5k and 6k rps on the API, with similar 5k/6k messages produced per second.
These results are regardless of using "SELECT ... FOR UPDATE" or advisory locks for concurrency control. I expect advisory locks to be more efficient in certain scenarios, but in many others, it'll be irrelevant.

Batch size can also influence throughput, so tweaks are in order to achieve the best results for a given scenario.
