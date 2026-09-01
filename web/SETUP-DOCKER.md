# Installing Docker for the local database

One-time, on the Legion, inside WSL2. Needs `sudo`, so it is a manual step rather than something
an agent can do unattended.

Docker Engine directly in the WSL distro, not Docker Desktop: Desktop is a Windows install with a
licence condition for larger organisations, and Engine inside the distro is fewer moving parts for
a database that only ever listens on localhost.

```bash
# 1. Repository and key
sudo apt-get update
sudo apt-get install -y ca-certificates curl gnupg
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
  | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg

echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
  | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

# 2. Engine plus the compose plugin
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io \
  docker-buildx-plugin docker-compose-plugin

# 3. Run docker without sudo. Log out and back in, or run: newgrp docker
sudo usermod -aG docker "$USER"

# 4. WSL2 has no systemd by default, so start the daemon
sudo service docker start
```

Verify:

```bash
docker run --rm hello-world
```

## Then bring up the database

```bash
cd web
npm run db:up
docker compose ps          # both services should read "healthy"
npm run db:migrate         # creates prisma/migrations/, applies the schema
npm run db:seed
```

## Notes

- **The daemon does not survive a WSL restart** unless systemd is enabled. Either run
  `sudo service docker start` after a reboot, or add `systemd=true` under `[boot]` in
  `/etc/wsl.conf` and `wsl --shutdown` from Windows once.
- **Both services bind to `127.0.0.1` only**, by design. The dev API is what gets exposed over the
  tailnet; the database never is.
- **Postgres data lives in a named volume**, so `docker compose down` keeps it and
  `docker compose down -v` deletes it.
- If port 5432 or 6379 is already taken, something else is running — check before remapping, since
  a second Postgres is usually the real problem.
