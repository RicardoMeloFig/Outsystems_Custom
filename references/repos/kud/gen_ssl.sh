#!/bin/bash

name=${1}
our_name=${name}
our_crt=${our_name}.crt
our_key=${our_name}.key
our_passkey=${our_name}.pass.key
our_csr=${our_name}.csr

country=US
state=Texas
locality=Austin
org=Out\ Systems
org_unit=IT
canon_name=k0s.local

mkdir ${our_name}
cd ${our_name}

openssl genrsa \
  -passout pass:x \
  -out ${our_passkey} \
  2048 \
  &> /dev/null

openssl rsa \
  -passin pass:x \
  -in ${our_passkey}\
  -out ${our_key} \
  &> /dev/null

openssl req \
  -new \
  -key ${our_key} \
  -out ${our_csr} \
  -subj "/C=${country}/ST=${state}/L=${locality}/O=${org}/OU=${org_unit}/CN=${canon_name}" \
  &> /dev/null

openssl x509 \
  -req \
  -sha256 \
  -days 365 \
  -in ${our_csr} \
  -signkey ${our_key} \
  -out ${our_crt} \
  &> /dev/null

if [[ "$OSTYPE" == "linux-gnu"* ]]; then
        # add
        sudo mkdir -p /usr/share/ca-certificates/extra
        sudo cp ${our_crt} /usr/share/ca-certificates/extra
        sudo dpkg-reconfigure ca-certificates
        grep extra /etc/ca-certificates.conf
        sudo update-ca-certificates
elif [[ "$OSTYPE" == "darwin"* ]]; then
        sudo security add-trusted-cert \
                -d \
                -r trustRoot \
                -k /Library/Keychains/System.keychain \
                ./${our_crt}
        sudo security find-certificate \
                -c ${our_name} \
                -a \
                -Z
fi

cd -
