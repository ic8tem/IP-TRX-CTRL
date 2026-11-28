#!/usr/bin/env bash
# misura.sh NOME SECONDI comando... : avvia il programma e misura memoria (RSS) e CPU dopo 20 s di avvio
name="$1"; secs="$2"; shift 2
"$@" > "/tmp/$name.log" 2>&1 &
pid=$!
sleep 20
if ! kill -0 $pid 2>/dev/null; then echo "$name: il programma si e' chiuso"; tail -5 "/tmp/$name.log"; exit 0; fi
# il processo vero (xvfb-run avvia un figlio)
real=$(pgrep -n -x IP-TRX-CTRL || echo $pid)
if [ ! -d /proc/$real ]; then echo "$name: processo non trovato"; tail -5 "/tmp/$name.log"; exit 0; fi
hz=$(getconf CLK_TCK)
st() { sed 's/.*) //' "$1" 2>/dev/null | awk '{print $12+$13}'; }   # utime+stime (il nome del thread può avere spazi)
p0=$(st /proc/$real/stat)
declare -A T0; for t in /proc/$real/task/*; do id=${t##*/}; T0[$id]=$(st $t/stat); done
t0=$(date +%s.%N); maxrss=0; sum=0; n=0
for i in $(seq 1 $((secs/2))); do
  sleep 2
  r=$(awk '/VmRSS/{print $2}' /proc/$real/status 2>/dev/null || echo 0)
  [ "$r" -gt "$maxrss" ] && maxrss=$r; sum=$((sum+r)); n=$((n+1))
done
p1=$(st /proc/$real/stat)
t1=$(date +%s.%N)
thr=$(awk '/Threads/{print $2}' /proc/$real/status)
echo "  thread più impegnati (CPU % di un core nella finestra di misura):"
for t in /proc/$real/task/*; do id=${t##*/}; c=$(st $t/stat); nm=$(cat $t/comm 2>/dev/null); b=${T0[$id]:-0}; echo "$(( ${c:-0}-b )) $nm"; done | sort -rn | head -6 | while read d nm; do printf "    %-20s %5.1f %%\n" "$nm" "$(echo "$d/$hz/($t1-$t0)*100" | bc -l)"; done
if [ -n "$TRACE" ]; then
  busy=$(for t in /proc/$real/task/*; do id=${t##*/}; c=$(st $t/stat); b=${T0[$id]:-0}; echo "$(( ${c:-0}-b )) $id"; done | sort -rn | head -2 | awk '{print $2}' | tr '\n' ',' | sed 's/,$//')
  for id in ${busy//,/ }; do echo "  thread $id: utime/stime = $(sed 's/.*) //' /proc/$real/task/$id/stat | awk '{print $12"/"$13}')  wchan=$(cat /proc/$real/task/$id/wchan 2>/dev/null)"; done
  echo "  perf sui thread più impegnati ($busy), 10 s:"
  sudo perf record -q -F 999 -g -t $busy -o /tmp/perf.data -- sleep 10 > /dev/null 2>&1
  sudo perf report -i /tmp/perf.data --stdio --no-children --sort symbol 2>/dev/null | grep -E "^ +[0-9.]+%" | head -30 | cut -c1-170 | sed 's/^/    /'
  echo "  chiamate di sistema (strace 5 s):"
  sudo timeout 5 strace -f -c -p $real 2>&1 | head -18 | sed 's/^/    /'
fi
echo "  ultime righe di sessioni.log:"; tail -n 12 ~/.config/IP-TRX-CTRL/sessioni.log 2>/dev/null | cut -c1-200 | sed 's/^/    /' 
cpu=$(echo "($p1-$p0)/$hz/($t1-$t0)*100" | bc -l | xargs printf "%.1f")
printf "%-34s RAM media %6.1f MB  massima %6.1f MB  CPU media %5s %% di un core  thread %s\n" "$name" "$(echo "$sum/$n/1024" | bc -l)" "$(echo "$maxrss/1024" | bc -l)" "$cpu" "$thr"
kill $pid 2>/dev/null; sleep 2; pkill -x IP-TRX-CTRL 2>/dev/null; sleep 1; true
