#!/usr/bin/env bash
# ==============================================================================
# irpotify — Universal One-Line Installer & Manager for Linux
# https://github.com/mohsenk2159/irpotify
# ==============================================================================

set -e

# Terminal colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
PURPLE='\033[0;35m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m' # No Color

INSTALL_DIR="/opt/irpotify"
REPO_URL="https://github.com/mohsenk2159/irpotify.git"
CLI_BIN="/usr/local/bin/irpotify"

# Docker Compose wrapper that automatically passes the compose files and .env
dc() {
    local compose_args=(--env-file "$INSTALL_DIR/.env" -f "$INSTALL_DIR/deploy/docker/docker-compose.yml" -f "$INSTALL_DIR/deploy/docker/docker-compose.lan.yml")
    docker compose "${compose_args[@]}" "$@"
}

# ------------------------------------------------------------------------------
# Banner
# ------------------------------------------------------------------------------
print_banner() {
    clear
    echo -e "${CYAN}${BOLD}"
    cat << "EOF"
 ██╗██████╗ ██████╗  ██████╗ ████████╗██╗███████╗██╗   ██╗
 ██║██╔══██╗██╔══██╗██╔═══██╗╚══██╔══╝██║██╔════╝╚██╗ ██╔╝
 ██║██████╔╝██████╔╝██║   ██║   ██║   ██║█████╗   ╚████╔╝ 
 ██║██╔══██╗██╔═══╝ ██║   ██║   ██║   ██║██╔══╝    ╚██╔╝  
 ██║██║  ██║██║     ╚██████╔╝   ██║   ██║██║        ██║   
 ╚═╝╚═╝  ╚═╝╚═╝      ╚═════╝    ╚═╝   ╚═╝╚═╝        ╚═╝   
EOF
    echo -e "${PURPLE}   === irpotify: Self-Hosted Music Streaming & Library Platform ===${NC}"
    echo -e "${BLUE}   === Modern, Dual-Port, Multi-User Audio Cloud ===${NC}"
    echo ""
}

# ------------------------------------------------------------------------------
# Privilege & Environment Checks
# ------------------------------------------------------------------------------
check_root() {
    if [ "$(id -u)" -ne 0 ]; then
        echo -e "${RED}[ERROR] This script must be run as root.${NC}"
        echo -e "${YELLOW}Please run with sudo:${NC}"
        echo -e "${GREEN}sudo bash $0${NC}"
        exit 1
    fi
}

detect_public_ip() {
    local ip
    ip=$(curl -s4 --connect-timeout 5 https://api.ipify.org 2>/dev/null || curl -s4 --connect-timeout 5 https://ifconfig.me 2>/dev/null || ip route get 1.1.1.1 2>/dev/null | awk '{print $7}')
    if [ -z "$ip" ]; then
        ip="127.0.0.1"
    fi
    echo "$ip"
}

# ------------------------------------------------------------------------------
# Dependencies Installation
# ------------------------------------------------------------------------------
install_dependencies() {
    echo -e "${BLUE}==>${NC} ${BOLD}Checking and installing system dependencies...${NC}"

    if command -v apt-get >/dev/null 2>&1; then
        apt-get update -y -qq
        apt-get install -y -qq curl git openssl ca-certificates socat cron >/dev/null 2>&1
    elif command -v dnf >/dev/null 2>&1; then
        dnf install -y -q curl git openssl ca-certificates socat crontabs >/dev/null 2>&1
    elif command -v yum >/dev/null 2>&1; then
        yum install -y -q curl git openssl ca-certificates socat crontabs >/dev/null 2>&1
    elif command -v pacman >/dev/null 2>&1; then
        pacman -Sy --noconfirm curl git openssl ca-certificates socat cronie >/dev/null 2>&1
    fi
    echo -e "${GREEN}✓${NC} System dependencies ready."
}

install_docker_if_needed() {
    if ! command -v docker >/dev/null 2>&1; then
        echo -e "${BLUE}==>${NC} ${BOLD}Docker is not installed. Installing official Docker Engine...${NC}"
        curl -fsSL https://get.docker.com | sh
        systemctl enable --now docker >/dev/null 2>&1 || service docker start >/dev/null 2>&1
        echo -e "${GREEN}✓${NC} Docker successfully installed and started."
    else
        echo -e "${GREEN}✓${NC} Docker is already installed."
    fi

    if ! docker compose version >/dev/null 2>&1; then
        echo -e "${YELLOW}!${NC} Checking and updating docker compose plugin..."
        if command -v apt-get >/dev/null 2>&1; then
            apt-get install -y -qq docker-compose-plugin >/dev/null 2>&1 || true
        fi
    fi
}

# ------------------------------------------------------------------------------
# Resilient Docker DNS Configuration (Fixes container build DNS failures)
# ------------------------------------------------------------------------------
configure_docker_dns() {
    echo -e "${BLUE}==>${NC} ${BOLD}Configuring resilient Docker DNS resolvers...${NC}"
    mkdir -p /etc/docker
    local daemon_json="/etc/docker/daemon.json"
    local needs_restart=false

    if [ ! -f "$daemon_json" ]; then
        cat << 'EOF' > "$daemon_json"
{
  "log-driver": "json-file",
  "log-opts": {
    "max-size": "10m",
    "max-file": "3"
  },
  "dns": ["1.1.1.1", "8.8.8.8", "1.0.0.1", "9.9.9.9"]
}
EOF
        needs_restart=true
    elif ! grep -q '"dns"' "$daemon_json" 2>/dev/null; then
        if command -v python3 >/dev/null 2>&1; then
            python3 -c '
import json
p = "/etc/docker/daemon.json"
try:
    with open(p, "r") as f:
        d = json.load(f)
    if "dns" not in d:
        d["dns"] = ["1.1.1.1", "8.8.8.8", "1.0.0.1", "9.9.9.9"]
        with open(p, "w") as f:
            json.dump(d, f, indent=2)
except Exception:
    pass
'
            needs_restart=true
        else
            sed -i '0,/{/s/{/{\n  "dns": ["1.1.1.1", "8.8.8.8", "1.0.0.1", "9.9.9.9"],/' "$daemon_json"
            needs_restart=true
        fi
    fi

    if [ "$needs_restart" = true ]; then
        echo -e "${YELLOW}Applying Docker DNS configuration...${NC}"
        systemctl restart docker >/dev/null 2>&1 || service docker restart >/dev/null 2>&1 || true
        sleep 2
    fi
    echo -e "${GREEN}✓${NC} Docker DNS resolvers ready."
}

# ------------------------------------------------------------------------------
# Helper for validating port numbers
# ------------------------------------------------------------------------------
validate_port() {
    local port="$1"
    if [[ "$port" =~ ^[0-9]+$ ]] && [ "$port" -ge 1024 ] && [ "$port" -le 65535 ]; then
        return 0
    fi
    return 1
}

# ------------------------------------------------------------------------------
# Automatic Firewall Configuration for User-Selected Ports
# ------------------------------------------------------------------------------
configure_firewall() {
    local d_port="$1"
    local p_port="$2"

    if command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null | grep -qw "active"; then
        echo -e "${BLUE}==>${NC} ${BOLD}Configuring UFW firewall rules for selected ports...${NC}"
        ufw allow 80/tcp >/dev/null 2>&1 || true
        ufw allow 443/tcp >/dev/null 2>&1 || true
        ufw allow "${d_port}/tcp" >/dev/null 2>&1 || true
        ufw allow "${p_port}/tcp" >/dev/null 2>&1 || true
        echo -e "${GREEN}✓${NC} UFW firewall: Ports ${d_port} and ${p_port} allowed."
    elif command -v firewall-cmd >/dev/null 2>&1 && systemctl is-active --quiet firewalld 2>/dev/null; then
        echo -e "${BLUE}==>${NC} ${BOLD}Configuring firewalld rules for selected ports...${NC}"
        firewall-cmd --permanent --add-port=80/tcp >/dev/null 2>&1 || true
        firewall-cmd --permanent --add-port=443/tcp >/dev/null 2>&1 || true
        firewall-cmd --permanent --add-port="${d_port}/tcp" >/dev/null 2>&1 || true
        firewall-cmd --permanent --add-port="${p_port}/tcp" >/dev/null 2>&1 || true
        firewall-cmd --reload >/dev/null 2>&1 || true
        echo -e "${GREEN}✓${NC} firewalld: Ports ${d_port} and ${p_port} allowed."
    fi
}

# ------------------------------------------------------------------------------
# Robust .env helper: replaces existing variable (even if commented out) or appends
# ------------------------------------------------------------------------------
set_env_var() {
    local key="$1"
    local value="$2"
    local file="${3:-.env}"
    if grep -q "^#\?[[:space:]]*${key}=" "$file" 2>/dev/null; then
        sed -i "s|^#\?[[:space:]]*${key}=.*|${key}=${value}|g" "$file"
    else
        echo "${key}=${value}" >> "$file"
    fi
}

# ------------------------------------------------------------------------------
# Installation Logic
# ------------------------------------------------------------------------------
setup_irpotify() {
    print_banner
    check_root
    install_dependencies
    install_docker_if_needed
    configure_docker_dns

    local server_ip
    server_ip=$(detect_public_ip)

    echo ""
    echo -e "${BOLD}${CYAN}────────────────────────────────────────────────────────────${NC}"
    echo -e "${BOLD}irpotify Server Setup Wizard (Two-Port Architecture):${NC}"
    echo -e "${BOLD}${CYAN}────────────────────────────────────────────────────────────${NC}"
    echo -e "irpotify operates 2 independent Web Applications on dedicated ports:"
    echo -e "  1. ${YELLOW}Admin Management Dashboard${NC} (Exclusively for Administrators)"
    echo -e "  2. ${GREEN}Player Web App${NC} (For users, listeners, Android, iOS and playback)"
    echo -e "${CYAN}────────────────────────────────────────────────────────────${NC}"
    echo ""

    echo -e "Detected Server Public IP: ${YELLOW}${BOLD}${server_ip}${NC}"
    echo ""

    # Mandatory prompt for Dashboard Port
    local dashboard_port=""
    while true; do
        read -r -p "Enter Port for Admin Dashboard [e.g. 5080]: " dashboard_port </dev/tty
        dashboard_port=$(echo "$dashboard_port" | tr -d '[:space:]')
        if validate_port "$dashboard_port"; then
            break
        fi
        echo -e "${RED}[ERROR] Invalid port. You must enter a number between 1024 and 65535.${NC}"
    done

    # Mandatory prompt for Player Web App Port
    local player_port=""
    while true; do
        read -r -p "Enter Port for Player Web App [e.g. 5081]: " player_port </dev/tty
        player_port=$(echo "$player_port" | tr -d '[:space:]')
        if ! validate_port "$player_port"; then
            echo -e "${RED}[ERROR] Invalid port. You must enter a number between 1024 and 65535.${NC}"
            continue
        fi
        if [ "$player_port" -eq "$dashboard_port" ]; then
            echo -e "${RED}[ERROR] Player port cannot be the same as Dashboard port ($dashboard_port). Choose a distinct port.${NC}"
            continue
        fi
        break
    done

    echo ""
    echo -e "Selected Ports: Dashboard=${YELLOW}${dashboard_port}${NC} | Player=${GREEN}${player_port}${NC}"
    echo ""

    configure_firewall "$dashboard_port" "$player_port"

    echo -e "Choose connection mode:"
    echo -e "  ${GREEN}1)${NC} ${BOLD}Custom Domain with Automatic Free SSL/HTTPS${NC} (Recommended for Internet)"
    echo -e "  ${GREEN}2)${NC} ${BOLD}Direct Server IP Address${NC} (For LAN, direct IP, or no domain)"
    echo ""

    local choice
    while true; do
        read -r -p "Select option (1 or 2) [Default: 2]: " choice </dev/tty || choice="2"
        choice=${choice:-2}
        if [[ "$choice" == "1" || "$choice" == "2" ]]; then
            break
        fi
        echo -e "${RED}Invalid selection. Please enter 1 or 2.${NC}"
    done

    local domain=""
    local email=""
    local use_domain=false

    if [ "$choice" == "1" ]; then
        use_domain=true
        while true; do
            read -r -p "Enter your domain or subdomain (e.g. music.yourdomain.com): " domain </dev/tty
            domain=$(echo "$domain" | tr -d '[:space:]')
            if [ -n "$domain" ]; then
                break
            fi
            echo -e "${RED}Domain is required.${NC}"
        done

        read -r -p "Enter your email (for Let's Encrypt SSL registration): " email </dev/tty
        email=$(echo "$email" | tr -d '[:space:]')
        email=${email:-"admin@${domain}"}
    fi

    echo ""
    echo -e "${BLUE}==>${NC} Setting up repository files in ${BOLD}${INSTALL_DIR}${NC}..."

    # Clone or sync repository
    if [ -d "$INSTALL_DIR/.git" ]; then
        echo -e "${YELLOW}Installation directory exists. Updating...${NC}"
        cd "$INSTALL_DIR"
        git fetch --all --prune
        git reset --hard origin/main
    else
        if [ -f "./MusicHub.slnx" ] && [ -f "./deploy/docker/docker-compose.yml" ]; then
            mkdir -p "$INSTALL_DIR"
            cp -r . "$INSTALL_DIR/"
            cd "$INSTALL_DIR"
        else
            mkdir -p "$INSTALL_DIR"
            git clone "$REPO_URL" "$INSTALL_DIR"
            cd "$INSTALL_DIR"
        fi
    fi

    echo -e "${BLUE}==>${NC} Configuring environment secrets and passwords..."
    local pg_password=""
    local jwt_secret=""
    local bootstrap_token=""

    # Preserve existing credentials if reinstalling or updating
    if [ -f "$INSTALL_DIR/.env" ]; then
        pg_password=$(grep -E '^POSTGRES_PASSWORD=' "$INSTALL_DIR/.env" 2>/dev/null | cut -d'=' -f2- | tr -d ' "\r\n' || true)
        jwt_secret=$(grep -E '^MUSICHUB_JWT_SECRET=' "$INSTALL_DIR/.env" 2>/dev/null | cut -d'=' -f2- | tr -d ' "\r\n' || true)
    fi

    if [ -z "$pg_password" ]; then
        pg_password=$(openssl rand -hex 32)
    fi
    if [ -z "$jwt_secret" ]; then
        jwt_secret=$(openssl rand -hex 64)
    fi
    bootstrap_token=$(openssl rand -hex 32)

    echo -e "${BLUE}==>${NC} Configuring environment file (.env)..."
    if [ ! -f .env ]; then
        cp .env.example .env
    fi

    # Apply configuration to .env using reliable set_env_var
    set_env_var "POSTGRES_PASSWORD" "${pg_password}"
    set_env_var "MUSICHUB_JWT_SECRET" "${jwt_secret}"
    set_env_var "MUSICHUB_BOOTSTRAP_TOKEN" "${bootstrap_token}"
    set_env_var "MUSICHUB_DASHBOARD_PORT" "${dashboard_port}"
    set_env_var "MUSICHUB_PLAYER_PORT" "${player_port}"
    set_env_var "MUSICHUB_LAN_ADDRESS" "0.0.0.0"

    local lan_ip
    lan_ip=$(hostname -I 2>/dev/null | awk '{print $1}')
    [ -z "$lan_ip" ] && lan_ip="127.0.0.1"
    set_env_var "MUSICHUB_HOST_LOCAL_IP" "${lan_ip}"
    set_env_var "MUSICHUB_HOST_PUBLIC_IP" "${server_ip}"

    if [ "$use_domain" = true ]; then
        set_env_var "MUSICHUB_PUBLIC_HOSTNAME" "${domain}"
        set_env_var "MUSICHUB_PUBLIC_URL" "https://${domain}"
        set_env_var "MUSICHUB_ACME_EMAIL" "${email}"
    else
        set_env_var "MUSICHUB_PUBLIC_HOSTNAME" "localhost"
        set_env_var "MUSICHUB_PUBLIC_URL" "http://${server_ip}:${player_port}"
        set_env_var "MUSICHUB_ACME_EMAIL" "admin@localhost"
    fi

    chmod 664 .env 2>/dev/null || chmod 600 .env

    # Setup automated port watcher for real-time Dashboard Settings port switching
    setup_port_sync_watcher

    # Install CLI command early so 'irpotify' command is available even if initial container start fails
    create_cli_command

    echo -e "${BLUE}==>${NC} Building and starting containers (this may take 2-3 minutes)..."
    if ! dc up -d --build; then
        echo ""
        echo -e "${RED}${BOLD}========================================================================${NC}"
        echo -e "${RED}${BOLD}               Container startup failed! Inspecting logs...             ${NC}"
        echo -e "${RED}${BOLD}========================================================================${NC}"
        echo ""
        echo -e "${YELLOW}${BOLD}--- API Container Logs (last 50 lines) ---${NC}"
        dc logs api --tail=50 || true
        echo ""
        echo -e "${YELLOW}${BOLD}--- PostgreSQL Container Logs (last 30 lines) ---${NC}"
        dc logs postgres --tail=30 || true
        echo ""
        echo -e "${RED}${BOLD}Diagnostics & Self-Healing:${NC}"
        echo -e "If the logs indicate ${YELLOW}'password authentication failed for user \"musichub\"'${NC},"
        echo -e "the existing PostgreSQL volume was initialized with an earlier password."
        echo -e "To perform a clean database volume reset, run:"
        echo -e "  ${CYAN}${BOLD}sudo irpotify reset-db${NC}"
        echo -e "Or manually:"
        echo -e "  ${CYAN}${BOLD}sudo irpotify down -v && sudo irpotify up -d${NC}"
        echo -e "${RED}${BOLD}========================================================================${NC}"
        exit 1
    fi

    echo ""
    echo -e "${GREEN}${BOLD}════════════════════════════════════════════════════════════════════${NC}"
    echo -e "${GREEN}${BOLD}    Congratulations! irpotify server is up and running!            ${NC}"
    echo -e "${GREEN}${BOLD}════════════════════════════════════════════════════════════════════${NC}"
    echo ""
    echo -e "👑 ${YELLOW}${BOLD}Admin Management Dashboard (Administrators):${NC}"
    if [ "$use_domain" = true ]; then
        echo -e "   URL:  ${CYAN}${BOLD}https://${domain}/index.html${NC}  or  ${CYAN}${BOLD}http://${server_ip}:${dashboard_port}/index.html${NC}"
    else
        echo -e "   URL:  ${CYAN}${BOLD}http://${server_ip}:${dashboard_port}/index.html${NC}"
    fi
    echo ""
    echo -e "🎧 ${GREEN}${BOLD}Player Web App (Users, Listeners & Playback):${NC}"
    if [ "$use_domain" = true ]; then
        echo -e "   URL:  ${CYAN}${BOLD}https://${domain}/player/${NC}  or  ${CYAN}${BOLD}http://${server_ip}:${player_port}/index.html${NC}"
    else
        echo -e "   URL:  ${CYAN}${BOLD}http://${server_ip}:${player_port}/index.html${NC}"
    fi
    echo ""
    echo -e "🔑 ${BOLD}One-Time Setup Token (Bootstrap Token):${NC}"
    echo -e "   ${YELLOW}${BOLD}${bootstrap_token}${NC}"
    echo ""
    echo -e "📌 ${BOLD}First-Time Setup Instructions:${NC}"
    echo -e "   1. Open the Admin Dashboard URL in your browser."
    echo -e "   2. Paste the ${BOLD}Bootstrap Token${NC} into the setup form."
    echo -e "   3. Enter your email and password to create the primary administrator."
    echo ""
    echo -e "🛠️  ${BOLD}Server Management Command:${NC}"
    echo -e "   Type ${GREEN}${BOLD}irpotify${NC} in your terminal anytime to manage ports, SSL, and server."
    echo -e "${GREEN}${BOLD}════════════════════════════════════════════════════════════════════${NC}"
    echo ""
}

# ------------------------------------------------------------------------------
# CLI Helper Creation
# ------------------------------------------------------------------------------
create_cli_command() {
    cat << 'EOF' > "$CLI_BIN"
#!/usr/bin/env bash
INSTALL_DIR="/opt/irpotify"
CLI_SCRIPT="$INSTALL_DIR/install.sh"

if [ ! -f "$CLI_SCRIPT" ]; then
    echo "irpotify installation not found in $INSTALL_DIR!"
    exit 1
fi

dc() {
    local compose_args=(--env-file "$INSTALL_DIR/.env" -f "$INSTALL_DIR/deploy/docker/docker-compose.yml" -f "$INSTALL_DIR/deploy/docker/docker-compose.lan.yml")
    docker compose "${compose_args[@]}" "$@"
}

case "$1" in
    status|ps)
        cd "$INSTALL_DIR" && dc ps
        ;;
    logs)
        shift
        cd "$INSTALL_DIR" && dc logs -f --tail="${1:-100}"
        ;;
    restart)
        cd "$INSTALL_DIR" && dc restart
        ;;
    start)
        cd "$INSTALL_DIR" && dc up -d
        ;;
    stop)
        cd "$INSTALL_DIR" && dc down
        ;;
    update)
        cd "$INSTALL_DIR"
        git fetch --all && git reset --hard origin/main
        dc up -d --build
        ;;
    ports)
        bash "$CLI_SCRIPT" --ports
        ;;
    ssl)
        bash "$CLI_SCRIPT" --ssl
        ;;
    reset-db)
        bash "$CLI_SCRIPT" --reset-db
        ;;
    uninstall)
        bash "$CLI_SCRIPT" --uninstall
        ;;
    credentials|token)
        grep -E "MUSICHUB_PUBLIC_URL|MUSICHUB_PUBLIC_HOSTNAME|MUSICHUB_BOOTSTRAP_TOKEN|MUSICHUB_DASHBOARD_PORT|MUSICHUB_PLAYER_PORT" "$INSTALL_DIR/.env" 2>/dev/null || true
        ;;
    reset-password)
        shift
        r_email="$1"
        r_pwd="$2"
        if [ -z "$r_pwd" ]; then
            echo -e "\033[1;36m=== irpotify Password Reset ===\033[0m"
            read -r -p "Enter user email (leave blank for primary admin): " r_email
            read -r -s -p "Enter new password (min 12 chars): " r_pwd
            echo ""
        fi
        if [ ${#r_pwd} -lt 12 ]; then
            echo -e "\033[0;31m[ERROR] Password must be at least 12 characters.\033[0m"
            exit 1
        fi
        token=$(grep "MUSICHUB_BOOTSTRAP_TOKEN" "$INSTALL_DIR/.env" | cut -d '=' -f2- | tr -d ' "\r\n')
        if [ -z "$token" ]; then
            echo -e "\033[0;31m[ERROR] Could not read MUSICHUB_BOOTSTRAP_TOKEN from $INSTALL_DIR/.env\033[0m"
            exit 1
        fi
        echo -e "\033[0;34mResetting password on server...\033[0m"
        if [ -n "$r_email" ]; then
            payload=$(printf '{"bootstrapToken":"%s","email":"%s","newPassword":"%s"}' "$token" "$r_email" "$r_pwd")
        else
            payload=$(printf '{"bootstrapToken":"%s","newPassword":"%s"}' "$token" "$r_pwd")
        fi
        cd "$INSTALL_DIR"
        resp=$(dc exec -T api curl -s -k -X POST http://localhost:5080/api/setup/reset-password \
            -H "Content-Type: application/json" \
            -d "$payload" 2>/dev/null || curl -s -k -X POST http://localhost:5080/api/setup/reset-password \
            -H "Content-Type: application/json" \
            -d "$payload" 2>/dev/null)
        echo -e "\033[0;32m$resp\033[0m"
        ;;
    --menu|"")
        bash "$CLI_SCRIPT" --menu
        ;;
    *)
        cd "$INSTALL_DIR" && dc "$@"
        ;;
esac
EOF
    chmod +x "$CLI_BIN"
}

# ------------------------------------------------------------------------------
# Change Ports Feature
# ------------------------------------------------------------------------------
manage_ports() {
    check_root
    cd "$INSTALL_DIR" || exit 1

    local current_d_port
    local current_p_port
    current_d_port=$(grep "^MUSICHUB_DASHBOARD_PORT=" .env 2>/dev/null | cut -d '=' -f2- | tr -d ' "\r\n')
    current_p_port=$(grep "^MUSICHUB_PLAYER_PORT=" .env 2>/dev/null | cut -d '=' -f2- | tr -d ' "\r\n')
    current_d_port=${current_d_port:-5080}
    current_p_port=${current_p_port:-5081}

    echo ""
    echo -e "${BOLD}${CYAN}────────────────────────────────────────────────────────────${NC}"
    echo -e "${BOLD}irpotify Port Configuration:${NC}"
    echo -e "${BOLD}${CYAN}────────────────────────────────────────────────────────────${NC}"
    echo -e "Current Admin Dashboard Port : ${YELLOW}${current_d_port}${NC}"
    echo -e "Current Player Web App Port  : ${GREEN}${current_p_port}${NC}"
    echo ""

    local new_d_port=""
    while true; do
        read -r -p "Enter new Admin Dashboard Port [$current_d_port]: " new_d_port </dev/tty || new_d_port="$current_d_port"
        new_d_port=${new_d_port:-$current_d_port}
        new_d_port=$(echo "$new_d_port" | tr -d '[:space:]')
        if validate_port "$new_d_port"; then
            break
        fi
        echo -e "${RED}[ERROR] Invalid port. You must enter a number between 1024 and 65535.${NC}"
    done

    local new_p_port=""
    while true; do
        read -r -p "Enter new Player Web App Port [$current_p_port]: " new_p_port </dev/tty || new_p_port="$current_p_port"
        new_p_port=${new_p_port:-$current_p_port}
        new_p_port=$(echo "$new_p_port" | tr -d '[:space:]')
        if ! validate_port "$new_p_port"; then
            echo -e "${RED}[ERROR] Invalid port. You must enter a number between 1024 and 65535.${NC}"
            continue
        fi
        if [ "$new_p_port" -eq "$new_d_port" ]; then
            echo -e "${RED}[ERROR] Player port cannot be the same as Dashboard port ($new_d_port). Choose a distinct port.${NC}"
            continue
        fi
        break
    done

    echo ""
    echo -e "${BLUE}Updating port configuration in .env...${NC}"
    set_env_var "MUSICHUB_DASHBOARD_PORT" "${new_d_port}"
    set_env_var "MUSICHUB_PLAYER_PORT" "${new_p_port}"
    set_env_var "MUSICHUB_LAN_ADDRESS" "0.0.0.0"

    # Update public url if it uses IP:old_port
    local server_ip
    server_ip=$(detect_public_ip)
    if grep -q "MUSICHUB_PUBLIC_URL=http://${server_ip}:" .env; then
        set_env_var "MUSICHUB_PUBLIC_URL" "http://${server_ip}:${new_p_port}"
    fi

    configure_firewall "$new_d_port" "$new_p_port"

    echo -e "${YELLOW}Restarting containers with new ports...${NC}"
    dc down
    dc up -d --force-recreate

    echo ""
    echo -e "${GREEN}✓ Ports successfully updated:${NC}"
    echo -e "   👑 Admin Dashboard : ${CYAN}http://${server_ip}:${new_d_port}/index.html${NC}"
    echo -e "   🎧 Player Web App  : ${CYAN}http://${server_ip}:${new_p_port}/index.html${NC}"
    echo ""
    read -r -p "Press Enter to continue..." </dev/tty
}

# ------------------------------------------------------------------------------
# Automated Port Reloader (Watches for Dashboard UI Port Changes)
# ------------------------------------------------------------------------------
setup_port_sync_watcher() {
    mkdir -p "$INSTALL_DIR/scripts"
    cat > "$INSTALL_DIR/scripts/reload-ports.sh" << 'EOF'
#!/usr/bin/env bash
set -e
INSTALL_DIR="/opt/irpotify"
[ -d "$INSTALL_DIR" ] || exit 0
cd "$INSTALL_DIR"

CONFIG_FILE="/var/lib/docker/volumes/musichub_data/_data/port_config.json"
if [ ! -f "$CONFIG_FILE" ]; then
    CONFIG_FILE="$INSTALL_DIR/data/port_config.json"
fi

if [ -f "$CONFIG_FILE" ]; then
    d_port=$(grep -o '"dashboardPort":[0-9]*' "$CONFIG_FILE" 2>/dev/null | cut -d':' -f2)
    p_port=$(grep -o '"playerPort":[0-9]*' "$CONFIG_FILE" 2>/dev/null | cut -d':' -f2)

    if [ -n "$d_port" ] && [ -n "$p_port" ]; then
        current_d_port=$(grep "^MUSICHUB_DASHBOARD_PORT=" .env 2>/dev/null | cut -d '=' -f2- | tr -d ' "\r\n')
        current_p_port=$(grep "^MUSICHUB_PLAYER_PORT=" .env 2>/dev/null | cut -d '=' -f2- | tr -d ' "\r\n')

        if [ "$d_port" != "$current_d_port" ] || [ "$p_port" != "$current_p_port" ]; then
            sed -i "s|^MUSICHUB_DASHBOARD_PORT=.*|MUSICHUB_DASHBOARD_PORT=${d_port}|g" .env
            sed -i "s|^MUSICHUB_PLAYER_PORT=.*|MUSICHUB_PLAYER_PORT=${p_port}|g" .env

            if command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null | grep -q "Status: active"; then
                ufw allow "${d_port}/tcp" >/dev/null 2>&1 || true
                ufw allow "${p_port}/tcp" >/dev/null 2>&1 || true
            fi

            docker compose --env-file "$INSTALL_DIR/.env" -f "$INSTALL_DIR/deploy/docker/docker-compose.yml" -f "$INSTALL_DIR/deploy/docker/docker-compose.lan.yml" up -d 2>/dev/null || true
        fi
    fi
fi
EOF
    chmod +x "$INSTALL_DIR/scripts/reload-ports.sh"

    if command -v systemctl >/dev/null 2>&1 && [ -d /run/systemd/system ]; then
        cat > /etc/systemd/system/irpotify-portsync.service << 'EOF'
[Unit]
Description=irpotify Port Configuration Reloader
After=docker.service

[Service]
Type=oneshot
ExecStart=/opt/irpotify/scripts/reload-ports.sh
EOF

        cat > /etc/systemd/system/irpotify-portsync.path << 'EOF'
[Unit]
Description=Watch for irpotify port configuration changes

[Path]
PathModified=/var/lib/docker/volumes/musichub_data/_data/port_config.json
PathModified=/opt/irpotify/.env

[Install]
WantedBy=multi-user.target
EOF

        systemctl daemon-reload >/dev/null 2>&1 || true
        systemctl enable --now irpotify-portsync.path >/dev/null 2>&1 || true
    fi
}

# ------------------------------------------------------------------------------
# Domain & SSL Management (Inspired by 3x-ui / acme.sh)
# ------------------------------------------------------------------------------
manage_domain_and_ssl() {
    check_root
    cd "$INSTALL_DIR" || exit 1

    local server_ip
    server_ip=$(detect_public_ip)
    local cur_host
    cur_host=$(grep "^MUSICHUB_PUBLIC_HOSTNAME=" .env 2>/dev/null | cut -d '=' -f2- | tr -d ' "\r\n')
    local cur_url
    cur_url=$(grep "^MUSICHUB_PUBLIC_URL=" .env 2>/dev/null | cut -d '=' -f2- | tr -d ' "\r\n')

    while true; do
        print_banner
        echo -e "${BOLD}${CYAN}────────────────────────────────────────────────────────────${NC}"
        echo -e "${BOLD}Domain, Public IP & SSL Certificate Management:${NC}"
        echo -e "${BOLD}${CYAN}────────────────────────────────────────────────────────────${NC}"
        echo -e "Detected Server Public IP : ${YELLOW}${server_ip}${NC}"
        echo -e "Currently Active Hostname : ${CYAN}${cur_host:-None}${NC}"
        echo -e "Currently Active URL      : ${GREEN}${cur_url:-None}${NC}"
        echo -e "${CYAN}────────────────────────────────────────────────────────────${NC}"
        echo -e "  ${GREEN}1)${NC} ${BOLD}Set Custom Domain & Automatic SSL (Let's Encrypt / ZeroSSL)${NC}"
        echo -e "  ${GREEN}2)${NC} ${BOLD}Issue SSL Certificate via acme.sh (Standalone HTTP-01 on Port 80)${NC}"
        echo -e "  ${GREEN}3)${NC} ${BOLD}Issue SSL Certificate via Cloudflare DNS API (For CDN / Wildcard)${NC}"
        echo -e "  ${GREEN}4)${NC} ${BOLD}Switch to Direct Server IP (Disable Domain)${NC}"
        echo -e "  ${YELLOW}0)${NC} Back to Main Menu"
        echo -e "${CYAN}────────────────────────────────────────────────────────────${NC}"
        echo ""

        local ssl_op
        read -r -p "Select an option [0-4]: " ssl_op </dev/tty
        echo ""

        case "$ssl_op" in
            1)
                echo -e "${BOLD}Configure Domain & Automatic SSL:${NC}"
                read -r -p "Enter domain or subdomain (e.g. music.example.com): " d_name </dev/tty
                d_name=$(echo "$d_name" | tr -d '[:space:]')
                if [ -z "$d_name" ]; then
                    echo -e "${RED}[ERROR] Domain cannot be empty.${NC}"
                    sleep 1
                    continue
                fi
                read -r -p "Enter your email for Let's Encrypt SSL: " a_email </dev/tty
                a_email=$(echo "$a_email" | tr -d '[:space:]')
                a_email=${a_email:-"admin@${d_name}"}

                set_env_var "MUSICHUB_PUBLIC_HOSTNAME" "${d_name}"
                set_env_var "MUSICHUB_PUBLIC_URL" "https://${d_name}"
                set_env_var "MUSICHUB_ACME_EMAIL" "${a_email}"

                echo -e "${YELLOW}Restarting services with domain SSL...${NC}"
                dc restart caddy 2>/dev/null || dc up -d caddy
                echo -e "${GREEN}✓ Domain and automatic SSL configured successfully.${NC}"
                echo -e "New URL: ${CYAN}https://${d_name}${NC}"
                read -r -p "Press Enter to continue..." </dev/tty
                return
                ;;
            2)
                echo -e "${BOLD}Issue SSL Certificate via acme.sh (Standalone):${NC}"
                echo -e "${YELLOW}Note: Port 80 must be free and the domain must point to this server IP.${NC}"
                read -r -p "Enter domain name: " acme_domain </dev/tty
                acme_domain=$(echo "$acme_domain" | tr -d '[:space:]')
                if [ -z "$acme_domain" ]; then
                    echo -e "${RED}[ERROR] Domain is required.${NC}"
                    sleep 1
                    continue
                fi

                # Install acme.sh if missing
                if [ ! -f "$HOME/.acme.sh/acme.sh" ]; then
                    echo -e "${BLUE}Installing acme.sh...${NC}"
                    curl -s https://get.acme.sh | sh -s email="admin@${acme_domain}"
                fi

                echo -e "${BLUE}Issuing SSL certificate for ${acme_domain}...${NC}"
                "$HOME/.acme.sh/acme.sh" --issue -d "$acme_domain" --standalone --force

                echo -e "${GREEN}✓ SSL certificate issued at $HOME/.acme.sh/${acme_domain}_ecc/${NC}"
                read -r -p "Press Enter to continue..." </dev/tty
                ;;
            3)
                echo -e "${BOLD}Issue SSL Certificate via Cloudflare DNS API:${NC}"
                read -r -p "Enter domain name: " cf_domain </dev/tty
                cf_domain=$(echo "$cf_domain" | tr -d '[:space:]')
                read -r -p "Cloudflare API Token or Global API Key: " cf_key </dev/tty
                cf_key=$(echo "$cf_key" | tr -d '[:space:]')
                read -r -p "Cloudflare Account Email (optional if using API Token): " cf_email </dev/tty
                cf_email=$(echo "$cf_email" | tr -d '[:space:]')

                if [ ! -f "$HOME/.acme.sh/acme.sh" ]; then
                    echo -e "${BLUE}Installing acme.sh...${NC}"
                    curl -s https://get.acme.sh | sh -s email="${cf_email:-admin@${cf_domain}}"
                fi

                export CF_Token="$cf_key"
                export CF_Key="$cf_key"
                export CF_Email="$cf_email"

                echo -e "${BLUE}Issuing wildcard certificate via Cloudflare DNS API...${NC}"
                "$HOME/.acme.sh/acme.sh" --issue --dns dns_cf -d "$cf_domain" -d "*.${cf_domain}" --force
                echo -e "${GREEN}✓ SSL certificate successfully obtained via Cloudflare.${NC}"
                read -r -p "Press Enter to continue..." </dev/tty
                ;;
            4)
                echo -e "${YELLOW}Switching to direct server IP access...${NC}"
                local p_port
                p_port=$(grep "^MUSICHUB_PLAYER_PORT=" .env 2>/dev/null | cut -d '=' -f2- | tr -d ' "\r\n')
                p_port=${p_port:-5081}

                set_env_var "MUSICHUB_PUBLIC_HOSTNAME" "localhost"
                set_env_var "MUSICHUB_PUBLIC_URL" "http://${server_ip}:${p_port}"
                set_env_var "MUSICHUB_ACME_EMAIL" "admin@localhost"

                dc restart
                echo -e "${GREEN}✓ Switched to direct IP mode.${NC}"
                read -r -p "Press Enter to continue..." </dev/tty
                return
                ;;
            0)
                return
                ;;
            *)
                echo -e "${RED}Invalid option.${NC}"
                sleep 1
                ;;
        esac
    done
}

# ------------------------------------------------------------------------------
# Reset Database Volume (Fixes password mismatch and reinitializes database)
# ------------------------------------------------------------------------------
reset_database_volume() {
    check_root
    echo -e "${RED}${BOLD}========================================================================${NC}"
    echo -e "${RED}${BOLD}                  Reset PostgreSQL Database Volume                      ${NC}"
    echo -e "${RED}${BOLD}========================================================================${NC}"
    echo -e "${YELLOW}This will wipe the current database container volume and re-initialize it${NC}"
    echo -e "${YELLOW}using the POSTGRES_PASSWORD defined in .env.${NC}"
    echo -e "${YELLOW}Uploaded music files and storage will NOT be deleted.${NC}"
    echo ""
    read -r -p "Are you sure you want to reset the database? (y/N): " confirm </dev/tty
    if [[ ! "$confirm" =~ ^[Yy]$ ]]; then
        echo -e "${YELLOW}Database reset cancelled.${NC}"
        read -r -p "Press Enter to continue..." </dev/tty
        return
    fi

    echo -e "${BLUE}Stopping containers and resetting PostgreSQL volume...${NC}"
    cd "$INSTALL_DIR"
    dc down -v
    echo -e "${BLUE}Starting containers with new database...${NC}"
    if ! dc up -d; then
        echo -e "${RED}[ERROR] Failed to start containers.${NC}"
        dc logs api --tail=30
    else
        echo -e "${GREEN}✓ Database volume successfully reset and containers started!${NC}"
    fi
    echo ""
    read -r -p "Press Enter to continue..." </dev/tty
}

# ------------------------------------------------------------------------------
# Complete Clean Uninstall Feature
# ------------------------------------------------------------------------------
uninstall_irpotify() {
    check_root
    echo -e "${RED}${BOLD}========================================================================${NC}"
    echo -e "${RED}${BOLD}                     100% Clean Uninstall irpotify                      ${NC}"
    echo -e "${RED}${BOLD}========================================================================${NC}"
    echo ""
    read -r -p "Are you sure you want to completely uninstall irpotify? (y/N): " confirm </dev/tty
    if [[ ! "$confirm" =~ ^[Yy]$ ]]; then
        echo -e "${YELLOW}Uninstall cancelled.${NC}"
        read -r -p "Press Enter to continue..." </dev/tty
        return
    fi

    echo ""
    echo -e "${YELLOW}Do you also want to permanently delete all music tracks and audio files?${NC}"
    echo -e "  - Enter ${BOLD}y${NC}: Delete everything including all music files permanently."
    echo -e "  - Enter ${BOLD}n${NC}: Keep music files (preserved at ${GREEN}/opt/irpotify_music_backup${NC})."
    echo ""
    read -r -p "Delete all music files too? (y/N): " purge_music </dev/tty

    echo ""
    echo -e "${BLUE}==>${NC} Stopping and removing all irpotify Docker containers..."
    if [ -d "$INSTALL_DIR" ]; then
        cd "$INSTALL_DIR"
        dc down -v --remove-orphans 2>/dev/null || docker compose down -v --remove-orphans 2>/dev/null || true
    fi

    local leftover_containers
    leftover_containers=$(docker ps -a -q --filter "name=musichub" 2>/dev/null || true)
    if [ -n "$leftover_containers" ]; then
        docker rm -f $leftover_containers >/dev/null 2>&1 || true
    fi

    if [[ "$purge_music" =~ ^[Yy]$ ]]; then
        echo -e "${BLUE}==>${NC} Purging all database and music volumes..."
        local musichub_volumes
        musichub_volumes=$(docker volume ls -q --filter "name=musichub" 2>/dev/null || true)
        if [ -n "$musichub_volumes" ]; then
            docker volume rm -f $musichub_volumes >/dev/null 2>&1 || true
        fi
        docker volume rm -f musichub_pgdata musichub_data 2>/dev/null || true
        rm -rf /opt/irpotify_music_backup
    else
        echo -e "${BLUE}==>${NC} Safely preserving music files to /opt/irpotify_music_backup..."
        mkdir -p /opt/irpotify_music_backup
        if [ -d "$INSTALL_DIR/storage/music" ]; then
            cp -r "$INSTALL_DIR/storage/music"/* /opt/irpotify_music_backup/ 2>/dev/null || true
        fi
        local vol_path
        vol_path=$(docker volume inspect musichub_data --format '{{.Mountpoint}}' 2>/dev/null || true)
        if [ -n "$vol_path" ] && [ -d "$vol_path/music" ]; then
            cp -r "$vol_path/music"/* /opt/irpotify_music_backup/ 2>/dev/null || true
        fi
        docker volume rm -f musichub_pgdata 2>/dev/null || true
    fi

    local musichub_networks
    musichub_networks=$(docker network ls -q --filter "name=musichub" 2>/dev/null || true)
    if [ -n "$musichub_networks" ]; then
        docker network rm $musichub_networks >/dev/null 2>&1 || true
    fi

    systemctl stop irpotify-portsync.path >/dev/null 2>&1 || true
    systemctl disable irpotify-portsync.path >/dev/null 2>&1 || true
    rm -f /etc/systemd/system/irpotify-portsync.service /etc/systemd/system/irpotify-portsync.path >/dev/null 2>&1 || true
    systemctl daemon-reload >/dev/null 2>&1 || true

    rm -f "$CLI_BIN"
    rm -rf "$INSTALL_DIR"

    echo ""
    echo -e "${GREEN}${BOLD}========================================================================${NC}"
    echo -e "${GREEN}${BOLD}    irpotify has been 100% completely uninstalled from this server!    ${NC}"
    echo -e "${GREEN}${BOLD}========================================================================${NC}"
    if [[ "$purge_music" =~ ^[Yy]$ ]]; then
        echo -e "✓ Containers: Removed"
        echo -e "✓ Database & Volumes: Removed"
        echo -e "✓ Installation Directory: Removed"
        echo -e "✓ Music Files: 100% Permanently Deleted"
    else
        echo -e "✓ Containers: Removed"
        echo -e "✓ Database Volume: Removed"
        echo -e "✓ Installation Directory: Removed"
        echo -e "✓ Music Files: ${GREEN}${BOLD}Preserved at /opt/irpotify_music_backup${NC}"
    fi
    echo ""
    echo -e "${CYAN}Running the install command will now start a 100% clean, fresh installation wizard:${NC}"
    echo -e "${GREEN}curl -fsSL https://raw.githubusercontent.com/mohsenk2159/irpotify/main/install.sh | sudo bash${NC}"
    echo -e "${GREEN}${BOLD}========================================================================${NC}"
    echo ""
    exit 0
}

# ------------------------------------------------------------------------------
# Interactive Management Menu
# ------------------------------------------------------------------------------
show_menu() {
    check_root
    cd "$INSTALL_DIR" || exit 1
    create_cli_command

    while true; do
        print_banner
        echo -e "${BOLD}${CYAN}irpotify Server Management Menu:${NC}"
        echo -e "${CYAN}────────────────────────────────────────────────────────────${NC}"
        echo -e "  ${GREEN}1)${NC} ${BOLD}View Service Status (Status)${NC}"
        echo -e "  ${GREEN}2)${NC} ${BOLD}View Live Server Logs (Live Logs)${NC}"
        echo -e "  ${GREEN}3)${NC} ${BOLD}Restart Services (Restart)${NC}"
        echo -e "  ${GREEN}4)${NC} ${BOLD}Stop Services (Stop)${NC}"
        echo -e "  ${GREEN}5)${NC} ${BOLD}Start Services (Start)${NC}"
        echo -e "  ${GREEN}6)${NC} ${BOLD}Update to Latest GitHub Version (Update)${NC}"
        echo -e "  ${GREEN}7)${NC} ${BOLD}View Credentials & Bootstrap Token (Credentials)${NC}"
        echo -e "  ${GREEN}8)${NC} ${BOLD}Reset Account Password (Reset Password)${NC}"
        echo -e "  ${PURPLE}9)${NC} ${BOLD}Change Listening Ports (Dashboard & Player Ports)${NC}"
        echo -e "  ${CYAN}10)${NC} ${BOLD}Domain & SSL Management (acme.sh / Caddy SSL)${NC}"
        echo -e "  ${YELLOW}11)${NC} ${YELLOW}${BOLD}Reset Database Volume (Fix password mismatch / Fresh DB)${NC}"
        echo -e "  ${RED}12)${NC} ${RED}${BOLD}Completely Uninstall irpotify (Clean Uninstall)${NC}"
        echo -e "  ${YELLOW}0)${NC} Exit"
        echo -e "${CYAN}────────────────────────────────────────────────────────────${NC}"
        echo ""

        local op
        read -r -p "Select an option [0-12]: " op </dev/tty
        echo ""

        case "$op" in
            1)
                echo -e "${BLUE}Running container status:${NC}"
                dc ps
                echo ""
                read -r -p "Press Enter to continue..." </dev/tty
                ;;
            2)
                echo -e "${BLUE}Streaming logs (Press Ctrl+C to exit):${NC}"
                dc logs -f --tail=100
                ;;
            3)
                echo -e "${YELLOW}Restarting services...${NC}"
                dc restart
                echo -e "${GREEN}✓ Services restarted.${NC}"
                read -r -p "Press Enter to continue..." </dev/tty
                ;;
            4)
                echo -e "${YELLOW}Stopping services...${NC}"
                dc down
                echo -e "${GREEN}✓ Services stopped.${NC}"
                read -r -p "Press Enter to continue..." </dev/tty
                ;;
            5)
                echo -e "${GREEN}Starting services...${NC}"
                dc up -d
                echo -e "${GREEN}✓ Services started successfully.${NC}"
                read -r -p "Press Enter to continue..." </dev/tty
                ;;
            6)
                echo -e "${BLUE}Pulling latest updates from GitHub...${NC}"
                git fetch --all
                git reset --hard origin/main
                echo -e "${BLUE}Rebuilding and restarting containers...${NC}"
                dc up -d --build
                create_cli_command
                echo -e "${GREEN}✓ Server updated successfully!${NC}"
                read -r -p "Press Enter to continue..." </dev/tty
                ;;
            7)
                echo -e "${BOLD}${CYAN}Server Configuration (.env):${NC}"
                echo -e "────────────────────────────────────────────────────────────"
                grep -E "MUSICHUB_PUBLIC_URL|MUSICHUB_PUBLIC_HOSTNAME|MUSICHUB_BOOTSTRAP_TOKEN|MUSICHUB_DASHBOARD_PORT|MUSICHUB_PLAYER_PORT" .env || true
                echo -e "────────────────────────────────────────────────────────────"
                echo ""
                read -r -p "Press Enter to continue..." </dev/tty
                ;;
            8)
                echo -e "${BOLD}${CYAN}Reset User / Admin Password:${NC}"
                read -r -p "Enter user email (leave blank for primary admin): " r_email </dev/tty
                read -r -s -p "Enter new password (min 12 chars): " r_pwd </dev/tty
                echo ""
                if [ ${#r_pwd} -lt 12 ]; then
                    echo -e "${RED}[ERROR] Password must be at least 12 characters.${NC}"
                else
                    token=$(grep "MUSICHUB_BOOTSTRAP_TOKEN" "$INSTALL_DIR/.env" | cut -d '=' -f2- | tr -d ' "\r\n')
                    if [ -z "$token" ]; then
                        echo -e "${RED}[ERROR] Could not read MUSICHUB_BOOTSTRAP_TOKEN from .env${NC}"
                    else
                        echo -e "${BLUE}Sending password reset request...${NC}"
                        if [ -n "$r_email" ]; then
                            payload=$(printf '{"bootstrapToken":"%s","email":"%s","newPassword":"%s"}' "$token" "$r_email" "$r_pwd")
                        else
                            payload=$(printf '{"bootstrapToken":"%s","newPassword":"%s"}' "$token" "$r_pwd")
                        fi
                        resp=$(dc exec -T api curl -s -k -X POST http://localhost:5080/api/setup/reset-password \
                            -H "Content-Type: application/json" \
                            -d "$payload" 2>/dev/null || curl -s -k -X POST http://localhost:5080/api/setup/reset-password \
                            -H "Content-Type: application/json" \
                            -d "$payload" 2>/dev/null)
                        echo -e "${GREEN}$resp${NC}"
                    fi
                fi
                echo ""
                read -r -p "Press Enter to continue..." </dev/tty
                ;;
            9)
                manage_ports
                ;;
            10)
                manage_domain_and_ssl
                ;;
            11)
                reset_database_volume
                ;;
            12)
                uninstall_irpotify
                ;;
            0)
                echo -e "${GREEN}Goodbye!${NC}"
                exit 0
                ;;
            *)
                echo -e "${RED}Invalid option.${NC}"
                sleep 1
                ;;
        esac
    done
}

# ------------------------------------------------------------------------------
# Main Entry Point
# ------------------------------------------------------------------------------
if [ "$1" == "--menu" ]; then
    show_menu
elif [ "$1" == "--ports" ]; then
    manage_ports
elif [ "$1" == "--ssl" ]; then
    manage_domain_and_ssl
elif [ "$1" == "--reset-db" ]; then
    reset_database_volume
elif [ "$1" == "--configure-dns" ]; then
    configure_docker_dns
elif [ "$1" == "--uninstall" ]; then
    uninstall_irpotify
elif [ -d "$INSTALL_DIR" ] && [ -f "$INSTALL_DIR/.env" ]; then
    if [ -d "$INSTALL_DIR/.git" ]; then
        ( cd "$INSTALL_DIR" && git fetch --all >/dev/null 2>&1 && git reset --hard origin/main >/dev/null 2>&1 ) || true
    fi
    create_cli_command
    show_menu
else
    setup_irpotify
fi
