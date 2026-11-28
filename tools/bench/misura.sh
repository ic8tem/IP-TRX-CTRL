#!/usr/bin/env bash
# misura.sh NOME SECONDI comando... : avvia il programma e misura memoria (RSS) e CPU dopo 20 s di avvio
name="$1"; secs="$2"; shift 2
"$@" > "/tmp/$name.log" 2>&1 &
pid=$!
sleep 20
if ! kill -0 $pid 2>/dev/null; then echo "$name: il programma si e' chiuso"; tail -5 "/tmp/$name.log"; exit 0; fi
# il processo vero (xvfb-run avvia un figlio)
real=$(pgrep -n -f "IP-TRX-CTRL" || echo $pid)
hz=$(getconf CLK_TCK)
read -r u0 s0 < <(awk '{print $14, $15}' /proc/$real/stat)
t0=$(date +%s.%N); maxrss=0; sum=0; n=0
for i in $(seq 1 $((secs/2))); do
  sleep 2
  r=$(awk '/VmRSS/{print $2}' /proc/$real/status 2>/dev/null || echo 0)
  [ "$r" -gt "$maxrss" ] && maxrss=$r; sum=$((sum+r)); n=$((n+1))
done
read -r u1 s1 < <(awk '{print $14, $15}' /proc/$real/stat)
t1=$(date +%s.%N)
thr=$(awk '/Threads/{print $2}' /proc/$real/status)
cpu=$(echo "scale=2; (($u1+$s1)-($u0+$s0))/$hz/($t1-$t0)*100" | bc)
printf "%-34s RAM media %6.1f MB  massima %6.1f MB  CPU media %5s %% di un core  thread %s\n" "$name" "$(echo "$sum/$n/1024" | bc -l)" "$(echo "$maxrss/1024" | bc -l)" "$cpu" "$thr"
kill $pid 2>/dev/null; sleep 2; pkill -f IP-TRX-CTRL 2>/dev/null; true
