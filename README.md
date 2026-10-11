# The Quiet Ward

A browser-based, third-person survival-horror game set in a fictionalized abandoned Beelitz military hospital.

The ward spans three explorable floors, joined by walkable switchback staircases, and opens onto an overgrown rooftop terrace. Ghostly patients wander the halls, while the Beast patrols between floors. Its pursuit now responds to player visibility and footsteps: sprinting carries farther, so break line of sight and hide when it investigates. A rising heartbeat and threat vignette warn when the Beast or Warden is close. Search the ward supply stores and furniture for water, food, and weapons. Two keycards are hidden in drawers and beneath beds; the last is carried by the towering Warden awakened by a fictional wartime fever-memory. Find the revolver in a second-floor ward desk by the main hall, survive the encounter, and shoot the Warden to recover the final card. The Warden is a supernatural game enemy, not a real historical person. Thirst and hunger gradually drain, and being depleted slows your walking pace. An iron pipe and flare pistol can briefly stun the Beast; the revolver is required to defeat the Warden.

## Run

In PowerShell, run:

```powershell
Set-Location C:\Users\ssar\Documents\BeelitzHorror
powershell -ExecutionPolicy Bypass -File .\run-game.ps1
```

Open <http://127.0.0.1:8000> in a modern browser. Stop the local server with **Ctrl+C**. To use another port, run `.\run-game.ps1 -Port 8080`. The server requests administrator permission so it can accept connections from other devices on your local network; accept the prompt, then use the LAN address printed by the server or the game's **COPY INVITE** button. If you only want to play on this computer, run `.\run-game.ps1 -LocalOnly`. Players must be able to reach the host over the network; allow the chosen TCP port through Windows Defender Firewall if prompted. Internet hosting may also require router port forwarding or a VPN. In the game's **SERVER** panel, enter the host address and matching room code to connect. Create Room generates a room code on the running server; Join Room connects to that same address and code. Separate room codes keep multiplayer movement, chat, and voice signaling in separate rooms. Voice requires microphone permission and a secure browser context (localhost or HTTPS); plain-HTTP LAN sessions still support multiplayer movement and text chat. An internet connection is needed to load Three.js and the optional display fonts.

Use **Profile / sign in** to choose a browser-saved guest name and coat color, or create/sign in to an account stored on the selected game server. Accounts are server-specific. Passwords are stored as salted PBKDF2 hashes; account sign-in is intended for HTTPS when used over the internet. Rations and water found in the ward are carried in the backpack and consumed with **EAT** and **DRINK**.

## Controls

- **Up / Down arrows:** Walk forward / backward.
- **Left / Right arrows:** Turn and look around.
- **Double-click anywhere:** Toggle sprint while holding **Up**; sprint consumes stamina, which regenerates when not sprinting.
- **Left mouse click:** Take targeted items; unlock the gatehouse; hide beside a bed or wardrobe; or toggle the flashlight.
- **Camera zoom:** Use the **+ / −** controls or the mouse wheel over the game to zoom in and out.
- **MAP button:** Open the floor map to locate yourself, the ward supply store, stairs, and gatehouse; select a floor to view its layout.
- **Ghost radar:** Read the live scanner at the lower-right of the HUD; it shows ghost contacts on your current floor within short range, relative to the direction you are facing.
- **Double-click a drawer:** Open it and search inside.
- **Triple-click beneath a bed:** Search under it.
- **Walk onto marked stairs:** Use the arrow keys to climb or descend the switchback stairs between all three floors.
- **Water / food meters or backpack buttons:** Click the water or food percentage to drink one carried water or eat one carried ration and restore that level.
- **Weapon buttons / right mouse click:** Equip the iron pipe or flare pistol to briefly stun the Beast. Equip the revolver, aim toward the Warden, and fire to defeat it.
- **SERVER button:** Set a host address and room code to join/create a private room, and copy a shareable invite. Start a self-hosted game server with `run-game.ps1`.
- **Brightness slider:** Adjust the game exposure (now up to 400%); the setting is saved in this browser.
- **Chat button:** Open the multiplayer text chat.
- **Enable voice / hold V:** Enable microphone audio and use push-to-talk.
- **Escape:** Leave a hiding place.

WASD and spacebar do not control the player. The ward supply store is in the east wing on every floor: the iron pipe is on the ground floor, the flare pistol and shells are on the second floor, and additional shells are on the third floor. Searchable furniture, keycards, and inventory are local to each player; chat, player movement, and optional voice are room-based. Recover the two hidden keycards, defeat the Warden for the third, and reach the ground-floor gatehouse to escape.

## Public hosting

The project currently provides a self-hosted HTTP/WebSocket server for local play. It is not yet packaged or deployed on Edgegap. A browser game also needs an HTTPS-served client and secure WSS multiplayer endpoint for public internet play; do not expose account sign-in over plain HTTP.
