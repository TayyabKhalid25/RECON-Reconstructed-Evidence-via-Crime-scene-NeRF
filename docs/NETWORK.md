# Network setup: Tailscale

Three people, three home networks, no port forwarding anywhere. Tailscale puts every machine
and phone on one private network (a tailnet) so the phone can hit a dev API on a laptop and
big `.ply` files move machine to machine without a cloud middleman.

## One-time setup

1. **One of us creates the tailnet** at tailscale.com (sign in with GitHub or Google). The
   free plan covers 3 users and 100 devices, which is exactly us. `verify`
2. **Invite the other two** from the admin console (Users > Invite).
3. **Everyone installs it** on: Windows (Legion, Victus, desktop), macOS (both MacBook Airs),
   and the AR test phones (Android/iOS apps). Log in to the same tailnet.
4. Turn on **MagicDNS** in the admin console so machines are reachable by name
   (`legion`, `victus`, `desktop`) instead of 100.x.x.x addresses.

## Daily use

- **Phone to dev API:** run the web app bound to all interfaces (`next dev -H 0.0.0.0`), then
  the phone opens `http://legion:3000`. Works from anywhere, not just the same wifi.
- **Big file transfer:** Taildrop (`tailscale file cp scene.ply victus:`) or just serve the
  folder. No drive links, no upload caps.
- **WSL2 caveat:** a service inside WSL2 is not automatically visible to the tailnet through
  the Windows Tailscale client. Easiest fix is installing Tailscale inside the WSL distro too
  (Linux install script) and bringing it up there; alternative is a Windows `netsh portproxy`
  from the Windows interface to the WSL address. Pick one, note it here. `verify`

## Rules

- The tailnet is the private network; the database and Redis stay on their managed hosts and
  are not moved behind it.
- Never expose the dev API to the public internet to work around a network problem; that is
  what this file exists to prevent.
