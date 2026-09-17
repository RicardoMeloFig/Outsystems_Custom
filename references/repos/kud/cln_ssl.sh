#!/bin/bash

name=${1}
our_name=${name}
our_crt=${our_name}.crt
our_key=${our_name}.key
our_passkey=${our_name}.pass.key
our_csr=${our_name}.csr

if [[ "$OSTYPE" == "linux-gnu"* ]]; then
        sudo rm /usr/share/ca-certificates/extra/${our_crt}
        sudo dpkg-reconfigure ca-certificates
        grep extra /etc/ca-certificates.conf
        sudo update-ca-certificates
elif [[ "$OSTYPE" == "darwin"* ]]; then
        sudo security find-certificate \
          -c ${our_name} \
          -a \
          -Z 
        sudo security delete-certificate \
          -c ${our_name}
fi

rm -rf ${our_name} 
