# Backend setup guide
## Notes
*To save file and close nano editor* press **CTRL+X** than **SHIFT+Y** than **ENTER**

## Setup
Required OS: **ubuntu 22.04+**

Recommended OS: **ubuntu 22.04 LTS**

## Update packges
```bash
sudo apt update
sudo apt upgrade
```

## Create user for backend app
```bash
sudo adduser explorer-backend
sudo usermod -aG sudo explorer-backend
```

## Create app directory
```bash
sudo mkdir /home/explorer-backend/server/
```

## Download and unpack backend build (change version in link and command to actual)
```bash
sudo wget https://github.com/steel97/veil-explorer/releases/download/latest/explorer-backend.linux-x86_64.self-contained.release-1.1.0.tar.gz
sudo tar -xzf explorer-backend.linux-x86_64.self-contained.release-1.1.0.tar.gz -C /home/explorer-backend/server/
```

## Issue permissions for backend
```bash
mkdir /home/explorer-backend/server/data
sudo chmod 755 /home/explorer-backend/server
sudo chmod 755 /home/explorer-backend/server/data
sudo chmod 777 /home/explorer-backend/server/explorer-backend
sudo chown -R explorer-backend /home/explorer-backend/server/
```

## Edit backend configuration
```bash
# Create configuration from template
cd /home/explorer-backend/server/
sudo wget https://raw.githubusercontent.com/steel97/veil-explorer/master/explorer-backend/appsettings.json.tpl
sudo mv /home/explorer-backend/server/appsettings.json.tpl /home/explorer-backend/server/appsettings.json

# Edit configuration
sudo nano /home/explorer-backend/server/appsettings.json
```
See: [/docs/backend-configuration.md](/docs/backend-configuration.md)

Now you can test backend:
```bash
su explorer-backend
cd /home/explorer-backend/server/
./explorer-backend
```
If there are no errors, move to next step.

## Register backend as systemd service
```bash
cd /home/explorer-backend/server/
wget https://raw.githubusercontent.com/steel97/veil-explorer/master/docs/systemd/explorer-backend.service
sudo systemctl link /home/explorer-backend/server/explorer-backend.service
```

## Finilize service creation
```bash
sudo systemctl daemon-reload
sudo systemctl enable explorer-backend.service
sudo systemctl start explorer-backend.service
sudo systemctl status explorer-backend.service
```

Done, now explorer backend is running as a background service. It will take some time to synchronize backend database with node.