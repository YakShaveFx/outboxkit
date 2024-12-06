# Simple end-to-end sample of OutboxKit.MySql.Polling usage

(in `MySqlEndToEndPollingSample` folder)

Start everything with Docker compose:

```sh
docker compose up -d --build
```

Open Grafana (`http://localhost:3000`) and create a new dashboard, importing the file `grafana-dashboard.json` from this folder.

Open RabbitMQ management UI, at `http://localhost:15672` (user:pass = guest:guest), to check has messages are sent.

Hammer the API using k6:
    
```sh
k6 run --vus 100 --duration 2m k6-script.js
```

With this setup, on my laptop, I can get around ~4k rps on the API, with also ~4k messages produced per second.