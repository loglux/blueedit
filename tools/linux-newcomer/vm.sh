#!/bin/sh
# Minimal Linux servers as virtual machines, for walk.py: the official cloud images (what a rented server or a
# server install without a desktop starts as), run by qemu with KVM inside a container, so the host gets nothing
# installed; the user needs /dev/kvm and Docker. Everything is in ~/be-vm (the images, checked against the published
# sums, stay for the next run). Each machine's ssh is on 127.0.0.1:PORT, user ubuntu or debian, key ~/be-vm/key.
#   vm.sh start      Ubuntu 24.04 minimal on 2201, Debian 12 genericcloud on 2202; returns when both answer
#   vm.sh stop       ends them and drops their disks (the images stay)
set -eu
D=~/be-vm
mkdir -p $D && cd $D
VMS="ubuntu-24.04:2201:ubuntu debian-12:2202:debian"

image() {   # name -> url sums-url sum-command
    case $1 in
        ubuntu-24.04) echo https://cloud-images.ubuntu.com/minimal/releases/noble/release/ubuntu-24.04-minimal-cloudimg-amd64.img \
                           https://cloud-images.ubuntu.com/minimal/releases/noble/release/SHA256SUMS sha256sum ;;
        debian-12)    echo https://cloud.debian.org/images/cloud/bookworm/latest/debian-12-genericcloud-amd64.qcow2 \
                           https://cloud.debian.org/images/cloud/bookworm/latest/SHA512SUMS sha512sum ;;
    esac
}

case ${1:-} in
start)
    if ! docker image inspect be-vm-tools >/dev/null 2>&1; then
        printf 'FROM debian:12\nRUN apt-get update && apt-get install -y --no-install-recommends qemu-system-x86 qemu-utils cloud-image-utils && rm -rf /var/lib/apt/lists/*\n' > Dockerfile
        docker build -q -t be-vm-tools . >/dev/null
    fi
    test -f key || ssh-keygen -q -t ed25519 -N "" -f key
    printf '#cloud-config\nssh_authorized_keys:\n  - %s\n' "$(cat key.pub)" > user-data
    for v in $VMS; do
        n=${v%%:*}; rest=${v#*:}; port=${rest%%:*}; user=${rest#*:}
        set -- $(image $n)
        if [ ! -f $n.qcow2 ]; then
            curl -fsSL -o $n.part "$1"
            curl -fsSL "$2" | grep -q "$($3 $n.part | cut -d' ' -f1)" || { echo "$n: the image's sum is not the published one" >&2; exit 1; }
            mv $n.part $n.qcow2
        fi
        docker rm -f be-vm-$n >/dev/null 2>&1 || true
        docker run --rm -v $D:/vm be-vm-tools sh -c "rm -f /vm/$n-disk.qcow2 /vm/$n-seed.img;
            qemu-img create -q -f qcow2 -F qcow2 -b /vm/$n.qcow2 /vm/$n-disk.qcow2 8G && cloud-localds /vm/$n-seed.img /vm/user-data"
        # virtio for the network too: Debian's cloud kernel has no driver for qemu's default card (e1000)
        docker run -d --name be-vm-$n --device /dev/kvm -p 127.0.0.1:$port:2222 -v $D:/vm be-vm-tools \
            qemu-system-x86_64 -enable-kvm -cpu host -m 2048 -smp 2 -nographic \
            -drive file=/vm/$n-disk.qcow2,if=virtio -drive file=/vm/$n-seed.img,if=virtio,format=raw \
            -nic user,model=virtio-net-pci,hostfwd=tcp::2222-:22 >/dev/null
    done
    for v in $VMS; do
        n=${v%%:*}; rest=${v#*:}; port=${rest%%:*}; user=${rest#*:}
        for i in $(seq 60); do
            ssh -q -o BatchMode=yes -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o ConnectTimeout=10 \
                -i key -p $port $user@127.0.0.1 'cloud-init status --wait >/dev/null 2>&1; true' && { echo "$n: up on $port"; break; }
            sleep 5
        done
    done ;;
stop)
    for v in $VMS; do n=${v%%:*}; docker rm -f be-vm-$n >/dev/null 2>&1 || true; done
    docker run --rm -v $D:/vm be-vm-tools sh -c 'rm -f /vm/*-disk.qcow2 /vm/*-seed.img' ;;
*)
    echo "usage: vm.sh start|stop" >&2; exit 2 ;;
esac
