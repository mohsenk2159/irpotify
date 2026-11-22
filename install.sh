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
    echo -e "${PURPLE}   === سامانه پخش و استریم موسیقی شخصی و ابری (irpotify) ===${NC}"
    echo -e "${BLUE}   === Self-Hosted Music Streaming & Library Platform ===${NC}"
    echo ""
}

# ------------------------------------------------------------------------------
# Privilege & Environment Checks
# ------------------------------------------------------------------------------
check_root() {
    if [ "$(id -u)" -ne 0 ]; then
        echo -e "${RED}[ERROR] این اسکریپت باید با دسترسی روت (root) اجرا شود.${NC}"
        echo -e "${YELLOW}لطفاً دستور را به صورت زیر اجرا کنید:${NC}"
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
    echo -e "${BLUE}==>${NC} ${BOLD}بررسی و نصب پیش‌نیازهای اولیه سیستم...${NC}"

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
    echo -e "${GREEN}✓${NC} پیش‌نیازهای پایه آماده هستند."
}

install_docker_if_needed() {
    if ! command -v docker >/dev/null 2>&1; then
        echo -e "${BLUE}==>${NC} ${BOLD}داکر نصب نیست. در حال نصب خودکار Docker Engine...${NC}"
        curl -fsSL https://get.docker.com | sh
        systemctl enable --now docker >/dev/null 2>&1 || service docker start >/dev/null 2>&1
        echo -e "${GREEN}✓${NC} داکر با موفقیت نصب و فعال شد."
    else
        echo -e "${GREEN}✓${NC} داکر از قبل روی سیستم نصب است."
    fi

    if ! docker compose version >/dev/null 2>&1; then
        echo -e "${YELLOW}!${NC} افزونه docker-compose در حال بررسی و به‌روزرسانی..."
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
    echo -e "${BOLD}پیکربندی نصب سرور irpotify:${NC}"
    echo -e "${BOLD}${CYAN}────────────────────────────────────────────────────────────${NC}"
    echo ""

    echo -e "آدرس IP عمومی شناسایی‌شده برای این سرور: ${YELLOW}${BOLD}${server_ip}${NC}"
    echo ""
    echo -e "روش اتصال به سرور را انتخاب کنید:"
    echo -e "  ${GREEN}1)${NC} ${BOLD}دامنه اختصاصی با SSL/HTTPS خودکار رایگان${NC} (پیشنهادی برای اینترنت)"
    echo -e "  ${GREEN}2)${NC} ${BOLD}آدرس IP مستقیم سرور${NC} (برای تست، شبکه داخلی یا بدون دامنه)"
    echo ""

    local choice
    while true; do
        read -r -p "گزینه مورد نظر (1 یا 2) [پیش‌فرض: 1]: " choice </dev/tty || choice="1"
        choice=${choice:-1}
        if [[ "$choice" == "1" || "$choice" == "2" ]]; then
            break
        fi
        echo -e "${RED}انتخاب نامعتبر است. لطفاً 1 یا 2 را وارد کنید.${NC}"
    done

    local domain=""
    local email=""
    local port="5080"
    local use_domain=false

    if [ "$choice" == "1" ]; then
        use_domain=true
        while true; do
            read -r -p "دامنه یا زیردامنه سرور (مثلاً music.yourdomain.com): " domain </dev/tty
            domain=$(echo "$domain" | tr -d '[:space:]')
            if [ -n "$domain" ]; then
                break
            fi
            echo -e "${RED}وارد کردن دامنه الزامی است.${NC}"
        done

        read -r -p "ایمیل شما (جهت ثبت گواهی SSL رایگان Let's Encrypt): " email </dev/tty
        email=$(echo "$email" | tr -d '[:space:]')
        email=${email:-"admin@${domain}"}
    else
        read -r -p "پورت وب و داشبورد سرور [پیش‌فرض: 5080]: " port </dev/tty || port="5080"
        port=${port:-5080}
    fi

    echo ""
    echo -e "${BLUE}==>${NC} در حال راه‌اندازی فایل‌های پروژه در مسیر ${BOLD}${INSTALL_DIR}${NC}..."

    # Clone or sync repository
    if [ -d "$INSTALL_DIR/.git" ]; then
        echo -e "${YELLOW}پوشه نصب از قبل وجود دارد. در حال به‌روزرسانی...${NC}"
        cd "$INSTALL_DIR"
        git fetch --all --prune
        git reset --hard origin/main
    else
        # If current folder has the repo files, copy them
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

    echo -e "${BLUE}==>${NC} تولید کلیدهای رمزنگاری امن و پسوردهای تصادفی..."
    local pg_password
    local jwt_secret
    local bootstrap_token
    pg_password=$(openssl rand -hex 32)
    jwt_secret=$(openssl rand -hex 64)
    bootstrap_token=$(openssl rand -hex 32)

    echo -e "${BLUE}==>${NC} ایجاد فایل تنظیمات .env..."
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

    echo -e "${BLUE}==>${NC} بیلد و استارت کانتینرها در داکر (این فرآیند ممکن است ۲ الی ۳ دقیقه طول بکشد)..."
    if [ "$use_domain" = true ]; then
        docker compose --env-file .env -f deploy/docker/docker-compose.yml up -d --build
    else
        docker compose --env-file .env -f deploy/docker/docker-compose.yml -f deploy/docker/docker-compose.lan.yml up -d --build
    fi

    # Create the helper CLI script
    create_cli_command

    echo ""
    echo -e "${GREEN}${BOLD}════════════════════════════════════════════════════════════════════${NC}"
    echo -e "${GREEN}${BOLD}        🎉 تبریک! سرور irpotify با موفقیت نصب و فعال شد          ${NC}"
    echo -e "${GREEN}${BOLD}════════════════════════════════════════════════════════════════════${NC}"
    echo ""
    if [ "$use_domain" = true ]; then
        echo -e "🌐 ${BOLD}آدرس ورود به پنل تحت وب:${NC}  ${CYAN}${BOLD}https://${domain}${NC}"
    else
        echo -e "🌐 ${BOLD}آدرس ورود به پنل تحت وب:${NC}  ${CYAN}${BOLD}http://${server_ip}:${port}${NC}"
    fi
    echo ""
    echo -e "🔑 ${BOLD}توکن راه‌اندازی اولیه (Bootstrap Token):${NC}"
    echo -e "   ${YELLOW}${BOLD}${bootstrap_token}${NC}"
    echo ""
    echo -e "📌 ${BOLD}اقدام لازم برای اولین ورود:${NC}"
    echo -e "   1. آدرس بالا را در مرورگر خود باز کنید."
    echo -e "   2. توکن بالا را در فیلد ${BOLD}Bootstrap Token${NC} وارد کنید."
    echo -e "   3. ایمیل و پسورد دلخواه خود را برای ساخت حساب ادمین وارد نمایید."
    echo ""
    echo -e "🛠️  ${BOLD}دستور مدیریت آسان سرور در هر زمان:${NC}"
    echo -e "   فقط کافیست در ترمینال کلمه ${GREEN}${BOLD}irpotify${NC} را تایپ کنید."
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

if [ -f "$CLI_SCRIPT" ]; then
    bash "$CLI_SCRIPT" --menu "$@"
else
    echo "پوشه نصب irpotify در $INSTALL_DIR یافت نشد!"
    exit 1
fi
EOF
    chmod +x "$CLI_BIN"
}

# ------------------------------------------------------------------------------
# Interactive Management Menu
# ------------------------------------------------------------------------------
show_menu() {
    check_root
    cd "$INSTALL_DIR" || exit 1

    while true; do
        print_banner
        echo -e "${BOLD}${CYAN}منوی مدیریت سرور irpotify:${NC}"
        echo -e "${CYAN}────────────────────────────────────────────────────────────${NC}"
        echo -e "  ${GREEN}1)${NC} ${BOLD}مشاهده وضعیت سرویس‌ها (Status)${NC}"
        echo -e "  ${GREEN}2)${NC} ${BOLD}مشاهده لاگ‌های زنده سرور (Live Logs)${NC}"
        echo -e "  ${GREEN}3)${NC} ${BOLD}راه‌اندازی مجدد سرویس‌ها (Restart)${NC}"
        echo -e "  ${GREEN}4)${NC} ${BOLD}توقف سرویس‌ها (Stop)${NC}"
        echo -e "  ${GREEN}5)${NC} ${BOLD}شروع و اجرای سرویس‌ها (Start)${NC}"
        echo -e "  ${GREEN}6)${NC} ${BOLD}به‌روزرسانی سرور به آخرین نسخه گیت‌هاب (Update)${NC}"
        echo -e "  ${GREEN}7)${NC} ${BOLD}مشاهده اطلاعات، آدرس‌ها و توکن راه‌اندازی (Credentials)${NC}"
        echo -e "  ${RED}8)${NC} ${RED}${BOLD}حذف کامل سرور irpotify (Uninstall)${NC}"
        echo -e "  ${YELLOW}0)${NC} خروج (Exit)"
        echo -e "${CYAN}────────────────────────────────────────────────────────────${NC}"
        echo ""

        local op
        read -r -p "شماره گزینه را وارد کنید [0-8]: " op </dev/tty
        echo ""

        case "$op" in
            1)
                echo -e "${BLUE}وضعیت کانتینرهای فعال:${NC}"
                docker compose ps
                echo ""
                read -r -p "برای ادامه Enter بزنید..." </dev/tty
                ;;
            2)
                echo -e "${BLUE}نمایش لاگ‌ها (برای خروج Ctrl+C را بزنید):${NC}"
                docker compose logs -f --tail=100
                ;;
            3)
                echo -e "${YELLOW}در حال ریستارت سرویس‌ها...${NC}"
                docker compose restart
                echo -e "${GREEN}✓ سرویس‌ها ریستارت شدند.${NC}"
                read -r -p "برای ادامه Enter بزنید..." </dev/tty
                ;;
            4)
                echo -e "${YELLOW}در حال متوقف‌سازی سرویس‌ها...${NC}"
                docker compose down
                echo -e "${GREEN}✓ سرویس‌ها متوقف شدند.${NC}"
                read -r -p "برای ادامه Enter بزنید..." </dev/tty
                ;;
            5)
                echo -e "${GREEN}در حال اجرای سرویس‌ها...${NC}"
                if grep -q "MUSICHUB_LAN_PORT" .env 2>/dev/null; then
                    docker compose --env-file .env -f deploy/docker/docker-compose.yml -f deploy/docker/docker-compose.lan.yml up -d
                else
                    docker compose --env-file .env -f deploy/docker/docker-compose.yml up -d
                fi
                echo -e "${GREEN}✓ سرویس‌ها با موفقیت اجرا شدند.${NC}"
                read -r -p "برای ادامه Enter بزنید..." </dev/tty
                ;;
            6)
                echo -e "${BLUE}در حال دریافت آخرین آپدیت‌ها از گیت‌هاب...${NC}"
                git fetch --all
                git reset --hard origin/main
                echo -e "${BLUE}بیلد مجدد کانتینرها...${NC}"
                if grep -q "MUSICHUB_LAN_PORT" .env 2>/dev/null; then
                    docker compose --env-file .env -f deploy/docker/docker-compose.yml -f deploy/docker/docker-compose.lan.yml up -d --build
                else
                    docker compose --env-file .env -f deploy/docker/docker-compose.yml up -d --build
                fi
                echo -e "${GREEN}✓ به‌روزرسانی با موفقیت انجام شد!${NC}"
                read -r -p "برای ادامه Enter بزنید..." </dev/tty
                ;;
            7)
                echo -e "${BOLD}${CYAN}اطلاعات پیکربندی سرور (.env):${NC}"
                echo -e "────────────────────────────────────────────────────────────"
                grep -E "MUSICHUB_PUBLIC_URL|MUSICHUB_PUBLIC_HOSTNAME|MUSICHUB_BOOTSTRAP_TOKEN|MUSICHUB_LAN_PORT" .env || true
                echo -e "────────────────────────────────────────────────────────────"
                echo ""
                read -r -p "برای ادامه Enter بزنید..." </dev/tty
                ;;
            8)
                read -r -p "آیا از حذف کامل سرور و پاک‌سازی کانتینرها اطمینان دارید؟ (y/N): " confirm </dev/tty
                if [[ "$confirm" =~ ^[Yy]$ ]]; then
                    docker compose down -v || true
                    rm -f "$CLI_BIN"
                    echo -e "${YELLOW}آیا داده‌های موسیقی و دیتابیس در $INSTALL_DIR نیز حذف شوند؟ (y/N): ${NC}"
                    read -r -p "" purge </dev/tty
                    if [[ "$purge" =~ ^[Yy]$ ]]; then
                        rm -rf "$INSTALL_DIR"
                        echo -e "${GREEN}✓ تمام فایل‌ها و سرور پاک‌سازی شدند.${NC}"
                        exit 0
                    fi
                    echo -e "${GREEN}✓ کانتینرها و سرویس متوقف و حذف شدند (پوشه $INSTALL_DIR حفظ شد).${NC}"
                    exit 0
                fi
                ;;
            0)
                echo -e "${GREEN}خداحافظ!${NC}"
                exit 0
                ;;
            *)
                echo -e "${RED}گزینه نامعتبر است.${NC}"
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
    # If already installed, jump straight to management menu
    show_menu
else
    setup_irpotify
fi
