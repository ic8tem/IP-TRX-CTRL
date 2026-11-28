#!/usr/bin/env bash
# serie.sh SECONDI comando... : CPU del programma ogni 5 s, per vedere quando consuma
secs="$1"; shift
"$@" > /tmp/serie.log 2>&1 &
pid=$!; hz=$(getconf CLK_TCK)
st() { sed 's/.*) //' "/proc/$pid/stat" 2>/dev/null | awk '{print $12+$13}'; }
prev=$(st); start=$(date +%s)
for i in $(seq 1 $((secs/5))); do
  sleep 5; cur=$(st); [ -z "$cur" ] && break
  printf "  %s  +%3ss  CPU %5.1f %%\n" "$(date -u +%H:%M:%S)" "$(( $(date +%s)-start ))" "$(echo "($cur-$prev)/$hz/5*100" | bc -l)"
  prev=$cur
done
kill $pid; sleep 2
grep -E "UPNP|PORTE prime|NAT" ~/.config/IP-TRX-CTRL/sessioni.log | tail -12 | cut -c1-140 | sed 's/^/  /'
