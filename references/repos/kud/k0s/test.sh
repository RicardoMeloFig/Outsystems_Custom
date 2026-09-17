#!/bin/bash

workernodes=$1
node=1

echo 'upstream workers {' > test.conf

while [ $node -le $workernodes ]
do
  echo -e "  server "$(docker inspect -f '{{range.NetworkSettings.Networks}}{{.IPAddress}}{{end}}' k0s-worker-$node)";" >> test.conf
  let node++
done

cat >>test.conf << EOL
}

server {
  listen 80;
  server_name www.k0s.local;
  location / {
    proxy_pass http://workers;
  }
}
EOL
