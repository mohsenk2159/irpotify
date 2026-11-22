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

# Docker Compose wrapper that automatically passes the correct compose files and .env
dc() {
    local compose_args=(--env-file "$INSTALL_DIR/.env" -f "$INSTALL_DIR/deploy/docker/docker-compose.yml")
    if grep -q "MUSICHUB_LAN_PORT" "$INSTALL_DIR/.env" 2>/dev/null; then
        compose_args+=(-f "$INSTALL_DIR/deploy/docker/docker-compose.lan.yml")
    fi
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
 ╚═╝╚═╝  ╚═╝╚═╝      ╚═════╝    ╚══╝   ╚═╝╚═╝        ╚═╝   
EOF
    echo -e "${PURPLE}   === irpotify: Self-Hosted Music Streaming & Library Platform ===${NC}"
    echo -e "${BLUE}   === Modern, Secure, Multi-User Audio Cloud ===${NC}"
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
        apt-get install -y -qq curl git openssl ca-certificates >/dev/null 2>&1
    elif command -v dnf >/dev/null 2>&1; then
        dnf install -y -q curl git openssl ca-certificates >/dev/null 2>&1
    elif command -v yum >/dev/null 2>&1; then
        yum install -y -q curl git openssl ca-certificates >/dev/null 2>&1
    elif command -v pacman >/dev/null 2>&1; then
        pacman -Sy --noconfirm curl git openssl ca-certificates >/dev/null 2>&1
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
# Installation Logic
# ------------------------------------------------------------------------------
setup_irpotify() {
    print_banner
    check_root
    install_dependencies
    install_docker_if_needed

    local server_ip
    server_ip=$(detect_public_ip)

    echo ""
    echo -e "${BOLD}${CYAN}────────────────────────────────────────────────────────────${NC}"
    echo -e "${BOLD}irpotify Server Setup Wizard:${NC}"
    echo -e "${BOLD}${CYAN}────────────────────────────────────────────────────────────${NC}"
    echo ""

    echo -e "Detected Server Public IP: ${YELLOW}${BOLD}${server_ip}${NC}"
    echo ""
    echo -e "Choose connection mode:"
    echo -e "  ${GREEN}1)${NC} ${BOLD}Custom Domain with Automatic Free SSL/HTTPS${NC} (Recommended)"
    echo -e "  ${GREEN}2)${NC} ${BOLD}Direct Server IP Address${NC} (For testing, LAN, or no domain)"
    echo ""

    local choice
    while true; do
        read -r -p "Select option (1 or 2) [Default: 1]: " choice </dev/tty || choice="1"
        choice=${choice:-1}
        if [[ "$choice" == "1" || "$choice" == "2" ]]; then
            break
        fi
        echo -e "${RED}Invalid selection. Please enter 1 or 2.${NC}"
    done

    local domain=""
    local email=""
    local port="5080"
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
    else
        read -r -p "Enter web dashboard port [Default: 5080]: " port </dev/tty || port="5080"
        port=${port:-5080}
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

    echo -e "${BLUE}==>${NC} Generating secure cryptographic secrets and passwords..."
    local pg_password
    local jwt_secret
    local bootstrap_token
    pg_password=$(openssl rand -hex 32)
    jwt_secret=$(openssl rand -hex 64)
    bootstrap_token=$(openssl rand -hex 32)

    echo -e "${BLUE}==>${NC} Configuring environment file (.env)..."
    cp .env.example .env

    # Apply configuration to .env
    sed -i "s|POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=${pg_password}|g" .env
    sed -i "s|MUSICHUB_JWT_SECRET=.*|MUSICHUB_JWT_SECRET=${jwt_secret}|g" .env
    sed -i "s|MUSICHUB_BOOTSTRAP_TOKEN=.*|MUSICHUB_BOOTSTRAP_TOKEN=${bootstrap_token}|g" .env

    if [ "$use_domain" = true ]; then
        sed -i "s|MUSICHUB_PUBLIC_HOSTNAME=.*|MUSICHUB_PUBLIC_HOSTNAME=${domain}|g" .env
        sed -i "s|MUSICHUB_PUBLIC_URL=.*|MUSICHUB_PUBLIC_URL=https://${domain}|g" .env
        sed -i "s|MUSICHUB_ACME_EMAIL=.*|MUSICHUB_ACME_EMAIL=${email}|g" .env
        sed -i "s|# MUSICHUB_LAN_ADDRESS=.*|MUSICHUB_LAN_ADDRESS=|g" .env
        sed -i "s|# MUSICHUB_LAN_PORT=.*|MUSICHUB_LAN_PORT=|g" .env
    else
        sed -i "s|MUSICHUB_PUBLIC_HOSTNAME=.*|MUSICHUB_PUBLIC_HOSTNAME=localhost|g" .env
        sed -i "s|MUSICHUB_PUBLIC_URL=.*|MUSICHUB_PUBLIC_URL=http://${server_ip}:${port}|g" .env
        sed -i "s|MUSICHUB_ACME_EMAIL=.*|MUSICHUB_ACME_EMAIL=admin@localhost|g" .env
        echo "MUSICHUB_LAN_ADDRESS=0.0.0.0" >> .env
        echo "MUSICHUB_LAN_PORT=${port}" >> .env
    fi

    chmod 600 .env

    echo -e "${BLUE}==>${NC} Building and starting containers (this may take 2-3 minutes)..."
    dc up -d --build

    # Create the helper CLI script
    create_cli_command

    echo ""
    echo -e "${GREEN}${BOLD}════════════════════════════════════════════════════════════════════${NC}"
    echo -e "${GREEN}${BOLD}    🎉 Congratulations! irpotify server is up and running!         ${NC}"
    echo -e "${GREEN}${BOLD}════════════════════════════════════════════════════════════════════${NC}"
    echo ""
    if [ "$use_domain" = true ]; then
        echo -e "🌐 ${BOLD}Web Dashboard URL:${NC}  ${CYAN}${BOLD}https://${domain}${NC}"
    else
        echo -e "🌐 ${BOLD}Web Dashboard URL:${NC}  ${CYAN}${BOLD}http://${server_ip}:${port}${NC}"
    fi
    echo ""
    echo -e "🔑 ${BOLD}One-Time Setup Token (Bootstrap Token):${NC}"
    echo -e "   ${YELLOW}${BOLD}${bootstrap_token}${NC}"
    echo ""
    echo -e "📌 ${BOLD}First-Time Setup Instructions:${NC}"
    echo -e "   1. Open the Web Dashboard URL above in your browser."
    echo -e "   2. Paste the ${BOLD}Bootstrap Token${NC} into the setup form."
    echo -e "   3. Enter your email and password to create the Administrator account."
    echo ""
    echo -e "🛠️  ${BOLD}Server Management Command:${NC}"
    echo -e "   Type ${GREEN}${BOLD}irpotify${NC} in your terminal anytime to manage this server."
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
    local compose_args=(--env-file "$INSTALL_DIR/.env" -f "$INSTALL_DIR/deploy/docker/docker-compose.yml")
    if grep -q "MUSICHUB_LAN_PORT" "$INSTALL_DIR/.env" 2>/dev/null; then
        compose_args+=(-f "$INSTALL_DIR/deploy/docker/docker-compose.lan.yml")
    fi
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
    credentials|token)
        grep -E "MUSICHUB_PUBLIC_URL|MUSICHUB_PUBLIC_HOSTNAME|MUSICHUB_BOOTSTRAP_TOKEN|MUSICHUB_LAN_PORT" "$INSTALL_DIR/.env" 2>/dev/null || true
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
        echo -e "  ${RED}8)${NC} ${RED}${BOLD}Completely Uninstall irpotify (Uninstall)${NC}"
        echo -e "  ${YELLOW}0)${NC} Exit"
        echo -e "${CYAN}────────────────────────────────────────────────────────────${NC}"
        echo ""

        local op
        read -r -p "Select an option [0-8]: " op </dev/tty
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
                grep -E "MUSICHUB_PUBLIC_URL|MUSICHUB_PUBLIC_HOSTNAME|MUSICHUB_BOOTSTRAP_TOKEN|MUSICHUB_LAN_PORT" .env || true
                echo -e "────────────────────────────────────────────────────────────"
                echo ""
                read -r -p "Press Enter to continue..." </dev/tty
                ;;
            8)
                read -r -p "Are you sure you want to completely uninstall irpotify? (y/N): " confirm </dev/tty
                if [[ "$confirm" =~ ^[Yy]$ ]]; then
                    dc down -v || true
                    rm -f "$CLI_BIN"
                    echo -e "${YELLOW}Do you also want to delete all music files and database in $INSTALL_DIR? (y/N): ${NC}"
                    read -r -p "" purge </dev/tty
                    if [[ "$purge" =~ ^[Yy]$ ]]; then
                        rm -rf "$INSTALL_DIR"
                        echo -e "${GREEN}✓ All files and services completely removed.${NC}"
                        exit 0
                    fi
                    echo -e "${GREEN}✓ Containers removed (data in $INSTALL_DIR preserved).${NC}"
                    exit 0
                fi
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
elif [ -d "$INSTALL_DIR" ] && [ -f "$INSTALL_DIR/.env" ]; then
    if [ -d "$INSTALL_DIR/.git" ]; then
        ( cd "$INSTALL_DIR" && git fetch --all >/dev/null 2>&1 && git reset --hard origin/main >/dev/null 2>&1 ) || true
    fi
    create_cli_command
    show_menu
else
    setup_irpotify
fi
