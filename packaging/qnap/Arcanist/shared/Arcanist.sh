#!/bin/sh
# Starts and stops Arcanist on QTS (App Center, boot and shutdown run it as root).
CONF=/etc/config/qpkg.conf
QPKG_NAME="Arcanist"
QPKG_ROOT=$(/sbin/getcfg $QPKG_NAME Install_Path -f $CONF)
# The data stays outside the package's folder: uninstalling or upgrading never deletes it.
DATA="${QPKG_ROOT%/.qpkg/*}/.arcanist"
PIDFILE=/var/run/arcanist.pid
export QNAP_QPKG=$QPKG_NAME

running() {
    [ -f $PIDFILE ] && kill -0 "$(cat $PIDFILE)" 2>/dev/null
}

case "$1" in
    start)
        ENABLED=$(/sbin/getcfg $QPKG_NAME Enable -u -d FALSE -f $CONF)
        if [ "$ENABLED" != "TRUE" ]; then
            echo "$QPKG_NAME is disabled."
            exit 1
        fi
        running && exit 0
        mkdir -p "$DATA"
        chmod 0750 "$DATA"
        [ -f "$DATA/package.conf" ] || echo "PORT=8484" > "$DATA/package.conf"
        # The package's page on QTS's web server (the root file system is rebuilt on every boot).
        mkdir -p /home/httpd/cgi-bin/qpkg
        ln -sfn "$QPKG_ROOT/ui" /home/httpd/cgi-bin/qpkg/$QPKG_NAME
        "$QPKG_ROOT/bin/arcanist-run" "$QPKG_ROOT" "$DATA" >>"$DATA/package.log" 2>&1 &
        echo $! > $PIDFILE
        ;;
    stop)
        if [ -f $PIDFILE ]; then
            PID=$(cat $PIDFILE)
            kill -TERM "$PID" 2>/dev/null
            i=0
            while kill -0 "$PID" 2>/dev/null && [ $i -lt 60 ]; do sleep 1; i=$((i+1)); done
            kill -9 "$PID" 2>/dev/null
            rm -f $PIDFILE
        fi
        ;;
    restart)
        $0 stop
        $0 start
        ;;
    status)
        if running; then echo "$QPKG_NAME is running."; else echo "$QPKG_NAME is stopped."; exit 1; fi
        ;;
    remove)
        rm -f /home/httpd/cgi-bin/qpkg/$QPKG_NAME
        ;;
    *)
        echo "Usage: $0 {start|stop|restart|status|remove}"
        exit 1
        ;;
esac
exit 0
