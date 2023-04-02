docker exec -i provisioning_bannou_1 bash
response=$(curl --write-out '%{http_code}' --output /dev/null 'bannou-service/testing/run/basic')
exit
echo "API response:" response
if [ response == 200 ]; then
    exit
fi
exit 1
